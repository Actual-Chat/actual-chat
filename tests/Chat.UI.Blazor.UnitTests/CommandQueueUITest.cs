using ActualChat.UI.Blazor.Services;
using ActualLab.Time.Testing;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class CommandQueueUITest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly Task<object?> NoResultTask = Task.FromResult<object?>(null);

    [Fact]
    public async Task SuccessfulCommandShouldStayVisibleWithItsSourceAndDuration()
    {
        // arrange
        using var clock = new TestClock();
        var queue = new CommandQueueUI(async (_, _) => {
            clock.OffsetBy(TimeSpan.FromMilliseconds(250));
            await Task.Yield();
            return 42;
        }, clock);

        // act
        var result = await queue.Run(new TestCommand("a"), CommandSource.UICommander);

        // assert
        result.Should().Be(42, because: "the caller gets what the command returned");
        var entry = queue.GetEntries("a").Single();
        entry.Stage.Should().Be(CommandStage.Completed, because: "a completed entry stays for a while");
        entry.Source.Should().Be(CommandSource.UICommander);
        entry.Duration.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(250),
            because: "it spans the whole delivery");
    }

    [Fact]
    public async Task PlainCommandShouldBeTrackedButNeitherOrderedNorRetried()
    {
        // arrange
        var tryCount = 0;
        var queue = new CommandQueueUI(
            (_, _) => {
                Interlocked.Increment(ref tryCount);
                throw new TimeoutException();
            },
            whenConnected: _ => TaskCompletionSourceExt.New<Unit>().Task);

        // act
        var resultTask = queue.Run(new PlainTestCommand());

        // assert
        await resultTask.Awaiting(x => x).Should().ThrowAsync<TimeoutException>(
            because: "a command that isn't IRetriedCommand neither waits for the connection nor retries");
        tryCount.Should().Be(1, because: "a timeout ends a command that isn't retried");
        var entry = queue.GetEntries().Single();
        entry.PartitionKey.Should().BeNull(because: "the command isn't IOrderedCommand");
        entry.Stage.Should().Be(CommandStage.Failed);
    }

    [Fact]
    public async Task OrderedCommandWithoutRetriesShouldFailAtOnceWithNoConnection()
    {
        // arrange
        var runCount = 0;
        var queue = new CommandQueueUI(
            (_, _) => {
                Interlocked.Increment(ref runCount);
                return NoResultTask;
            },
            isConnected: () => false);

        // act
        var firstTask = queue.Run(new OrderedTestCommand("a"));
        var secondTask = queue.Run(new OrderedTestCommand("a"));

        // assert
        await firstTask.Awaiting(x => x).Should().ThrowAsync<Exception>(
            because: "an attempt would only wait for its connect timeout, holding up the lane");
        await secondTask.Awaiting(x => x).Should().ThrowAsync<Exception>(
            because: "the lane moves on to the next command, which fails the same way");
        runCount.Should().Be(0, because: "neither command reaches the server");
    }

    [Fact]
    public async Task PauseShouldFailOrderedCommandWithoutRetriesAsNoConnectionWould()
    {
        // arrange
        var runCount = 0;
        var queue = new CommandQueueUI((_, _) => {
            Interlocked.Increment(ref runCount);
            return NoResultTask;
        });
        queue.Pause();

        // act
        var resultTask = queue.Run(new OrderedTestCommand("a"));

        // assert
        await resultTask.Awaiting(x => x).Should().ThrowAsync<Exception>(
            because: "a pause stands for a lost connection");
        runCount.Should().Be(0, because: "the command never reaches the server");
    }

    [Fact]
    public async Task CancelledCallShouldEndCancelledAndShowAsFailed()
    {
        // arrange
        using var cts = new CancellationTokenSource();
        var queue = new CommandQueueUI(async (_, ct) => {
            await Task.Delay(System.Threading.Timeout.Infinite, ct).ConfigureAwait(false);
            return null;
        });

        // act
        var resultTask = queue.Run(new TestCommand("a"), CommandSource.Direct, cts.Token);
        cts.Cancel();

        // assert
        await TestWait.WhenPolled(() => resultTask.IsCanceled.Should().BeTrue(
            because: "the caller's own cancellation isn't retried"));
        queue.GetEntries("a").Single().Stage.Should().Be(CommandStage.Failed);
    }

    [Fact]
    public async Task PermanentFailureShouldBeKeptAsFailedAndReported()
    {
        // arrange
        using var clock = new TestClock();
        var queue = new CommandQueueUI((_, _) => throw new InvalidOperationException("nope"), clock);

        // act
        var resultTask = queue.Run(new TestCommand("a"));

        // assert
        var entry = queue.GetEntries("a").Single();
        entry.Stage.Should().Be(CommandStage.Failed, because: "a permanent failure isn't retried");
        entry.Error.Should().BeOfType<InvalidOperationException>(because: "the entry keeps what went wrong");
        await resultTask.Awaiting(x => x).Should().ThrowAsync<InvalidOperationException>(
            because: "the caller has to learn the command didn't get through");
    }

    [Fact]
    public async Task TransientFailureShouldRetryAndCountTries()
    {
        // arrange
        using var clock = new TestClock();
        var tryCount = 0;
        var queue = new CommandQueueUI((_, _) => {
            tryCount++;
            return tryCount < 3 ? throw new TimeoutException() : NoResultTask;
        }, clock) { RetryDelay = TimeSpan.Zero };

        // act
        await queue.Run(new TestCommand("a"));

        // assert
        tryCount.Should().Be(3, because: "two transient failures are retried, and only then the task completes");
        var entry = queue.GetEntries("a").Single();
        entry.Stage.Should().Be(CommandStage.Completed);
        entry.TryIndex.Should().Be(2, because: "the try index counts the retries");
    }

    [Fact]
    public async Task FinishedEntriesShouldExpireOnTheirOwnAfterTtl()
    {
        // arrange
        using var clock = new TestClock();
        var queue = new CommandQueueUI(
            (command, _) => ((TestCommand)command).Name == "fail"
                ? throw new InvalidOperationException()
                : NoResultTask,
            clock);
        _ = queue.Run(new TestCommand("a"));
        _ = queue.Run(new TestCommand("b", "fail"));

        // act
        clock.OffsetBy(CommandQueueUI.CompletedTtl + TimeSpan.FromSeconds(1));

        // assert
        await TestWait.WhenPolled(() => queue.GetEntries("a").Should().BeEmpty(
            because: "a completed entry expires after its TTL with nobody reading the queue"));
        queue.GetEntries("b").Should().HaveCount(1, because: "a failed entry stays longer");

        clock.OffsetBy(CommandQueueUI.FailedTtl);
        await TestWait.WhenPolled(() => queue.GetEntries("b").Should().BeEmpty(
            because: "a failed entry expires after its own TTL"));
    }

    [Fact]
    public async Task OnePartitionShouldRunOneCommandAtATimeInOrder()
    {
        // arrange
        var gate = TaskCompletionSourceExt.New<Unit>();
        var runs = new ConcurrentQueue<string>();
        var queue = new CommandQueueUI(async (command, _) => {
            runs.Enqueue(((TestCommand)command).Name);
            await gate.Task.ConfigureAwait(false);
            return null;
        });

        // act
        _ = queue.Run(new TestCommand("a", "1"));
        _ = queue.Run(new TestCommand("a", "2"));
        _ = queue.Run(new TestCommand("a", "3"));

        // assert
        runs.Should().Equal(["1"], because: "the lane holds the rest until the running one completes");

        gate.SetResult(default);
        await TestWait.WhenPolled(() => runs.Should().Equal(["1", "2", "3"],
            because: "the lane drains in the order the commands were queued"));
    }

    [Fact]
    public async Task ReplaceWaitingShouldCollapseTheBacklog()
    {
        // arrange
        var gate = TaskCompletionSourceExt.New<Unit>();
        var runs = new ConcurrentQueue<string>();
        var queue = new CommandQueueUI(async (command, _) => {
            runs.Enqueue(((CoalescingTestCommand)command).Name);
            await gate.Task.ConfigureAwait(false);
            return null;
        });

        // act
        _ = queue.Run(new CoalescingTestCommand("a", "1"));
        var replacedTask = queue.Run(new CoalescingTestCommand("a", "2"));
        _ = queue.Run(new CoalescingTestCommand("a", "3"));

        // assert
        replacedTask.IsCompletedSuccessfully.Should().BeTrue(
            because: "a replaced command is done: the latest one carries its intent");
        gate.SetResult(default);
        await TestWait.WhenPolled(() => runs.Should().Equal(["1", "3"],
            because: "the two waiting commands collapse into the latest one"));
        queue.GetEntries("a").Select(x => ((CoalescingTestCommand)x.Command).Name)
            .Should().Equal(["1", "3"], because: "a replaced command leaves the registry too");
    }

    [Fact]
    public void DifferentPartitionsShouldNotBlockEachOther()
    {
        // arrange
        var runCount = 0;
        var gate = TaskCompletionSourceExt.New<Unit>();
        var queue = new CommandQueueUI(async (_, _) => {
            Interlocked.Increment(ref runCount);
            await gate.Task.ConfigureAwait(false);
            return null;
        });

        // act
        _ = queue.Run(new TestCommand("a"));
        _ = queue.Run(new TestCommand("b"));
        _ = queue.Run(new TestCommand(""));
        _ = queue.Run(new TestCommand(""));
        _ = queue.Run(new TestCommand(null!));

        // assert
        runCount.Should().Be(5,
            because: "only one partition is ordered; an empty or missing key asks for retries, not for a lane");
        gate.SetResult(default);
    }

    [Fact]
    public async Task PausedQueueShouldHoldCommandsUntilResumed()
    {
        // arrange
        var runCount = 0;
        var queue = new CommandQueueUI((_, _) => {
            Interlocked.Increment(ref runCount);
            return NoResultTask;
        });
        queue.Pause();

        // act
        _ = queue.Run(new TestCommand("a"));
        _ = queue.Run(new TestCommand("a"));
        _ = queue.Run(new TestCommand("b"));

        // assert
        queue.IsPaused.Should().BeTrue(because: "Pause takes effect at once");
        runCount.Should().Be(0, because: "a paused queue behaves like a lost connection");
        queue.GetEntries().Should().OnlyContain(x => x.Stage == CommandStage.Waiting,
            because: "commands are held before their first attempt");

        queue.Resume();
        await TestWait.WhenPolled(() => runCount.Should().Be(3, because: "resuming releases the whole backlog"));
    }

    [Fact]
    public async Task NoConnectionShouldHoldCommandsUntilConnected()
    {
        // arrange
        var runCount = 0;
        var connected = TaskCompletionSourceExt.New<Unit>();
        var queue = new CommandQueueUI(
            (_, _) => {
                Interlocked.Increment(ref runCount);
                return NoResultTask;
            },
            whenConnected: ct => connected.Task.WaitAsync(ct));

        // act
        var resultTask = queue.Run(new TestCommand("a"));

        // assert
        runCount.Should().Be(0, because: "an attempt with no connection would only wait for its timeout");
        resultTask.IsCompleted.Should().BeFalse(because: "the command hasn't got through yet");

        connected.SetResult(default);
        await resultTask;
        runCount.Should().Be(1, because: "the command goes out once the connection is back");
    }

    [Fact]
    public async Task PauseShouldNotInterruptTheRunningCommand()
    {
        // arrange
        var hasStarted = TaskCompletionSourceExt.New<Unit>();
        var serverReply = TaskCompletionSourceExt.New<Unit>();
        var queue = new CommandQueueUI(async (_, _) => {
            hasStarted.TrySetResult(default);
            await serverReply.Task.ConfigureAwait(false);
            return null;
        });

        // act
        _ = queue.Run(new TestCommand("a"));
        await hasStarted.Task;
        queue.Pause();
        serverReply.SetResult(default);

        // assert
        await TestWait.WhenPolled(() => queue.GetEntries("a").Single().Stage
            .Should().Be(CommandStage.Completed, because: "an attempt already in flight is left to finish"));
    }

    [Fact]
    public async Task DisposeShouldStopRetryingWithoutReportingFailure()
    {
        // arrange
        var tryCount = 0;
        var queue = new CommandQueueUI((_, _) => {
            Interlocked.Increment(ref tryCount);
            throw new TimeoutException();
        }) { RetryDelay = TimeSpan.FromMilliseconds(10) };
        var retriedTask = queue.Run(new TestCommand("a"));
        var waitingTask = queue.Run(new TestCommand("a"));
        await TestWait.WhenPolled(() => tryCount.Should().BeGreaterThan(1));

        // act
        queue.Dispose();
        var stoppedAt = tryCount;
        await Task.Delay(100);

        // assert
        tryCount.Should().BeLessThanOrEqualTo(stoppedAt + 1, because: "a disposed queue stops retrying");
        await TestWait.WhenPolled(() => retriedTask.IsCanceled.Should().BeTrue(
            because: "a stop isn't a failure the user has to hear about"));
        waitingTask.IsCanceled.Should().BeTrue(because: "a command that never ran ends with the queue too");
    }

    // Nested types

    private sealed record TestCommand(string PartitionKey, string Name = "") : IOrderedCommand, IRetriedCommand;

    private sealed record OrderedTestCommand(string PartitionKey) : IOrderedCommand;

    private sealed record PlainTestCommand : ICommand<int>;

    private sealed record CoalescingTestCommand(string PartitionKey, string Name) : IOrderedCommand, IRetriedCommand
    {
        public CommandCoalescing Coalescing => CommandCoalescing.ReplaceWaiting;
    }
}
