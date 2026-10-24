using ActualChat.UI.Blazor.Services;
using ActualLab.Time.Testing;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class CommandQueueUITest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void SuccessfulCommandShouldBeKeptUntilConfirmed()
    {
        // arrange
        using var clock = new TestClock();
        var queue = new CommandQueueUI((_, _) => Task.CompletedTask, clock);
        var command = new TestCommand("a");

        // act
        queue.Enqueue(command);

        // assert
        var entries = queue.GetEntries("a");
        entries.Should().HaveCount(1);
        entries[0].Stage.Should().Be(QueuedCommandStage.Completed,
            because: "the entry survives until a consumer confirms it");

        queue.Confirm(command);
        queue.GetEntries("a").Should().BeEmpty(because: "confirmation drops the entry");
    }

    [Fact]
    public void PermanentFailureShouldBeKeptAsFailedAndReported()
    {
        // arrange
        using var clock = new TestClock();
        var reported = new List<Exception>();
        var queue = new CommandQueueUI(
            (_, _) => throw new InvalidOperationException("nope"), clock, reported.Add);

        // act
        queue.Enqueue(new TestCommand("a"));

        // assert
        var entry = queue.GetEntries("a").Single();
        entry.Stage.Should().Be(QueuedCommandStage.Failed, because: "a permanent failure isn't retried");
        entry.Error.Should().BeOfType<InvalidOperationException>(because: "the entry keeps what went wrong");
        reported.Should().ContainSingle(because: "the user has to learn the command didn't get through");
    }

    [Fact]
    public void TransientFailureShouldRetryAndCountTries()
    {
        // arrange
        using var clock = new TestClock();
        var tryCount = 0;
        var queue = new CommandQueueUI((_, _) => {
            tryCount++;
            return tryCount < 3 ? throw new TimeoutException() : Task.CompletedTask;
        }, clock) { RetryDelay = TimeSpan.Zero };

        // act
        queue.Enqueue(new TestCommand("a"));

        // assert
        tryCount.Should().Be(3, because: "two transient failures are retried");
        var entry = queue.GetEntries("a").Single();
        entry.Stage.Should().Be(QueuedCommandStage.Completed);
        entry.TryIndex.Should().Be(2, because: "the try index counts the retries");
    }

    [Fact]
    public void CompletedEntryShouldExpireAfterTtl()
    {
        // arrange
        using var clock = new TestClock();
        var queue = new CommandQueueUI((_, _) => Task.CompletedTask, clock);
        queue.Enqueue(new TestCommand("a"));

        // act
        clock.OffsetBy(TimeSpan.FromSeconds(11));

        // assert
        queue.GetEntries("a").Should().BeEmpty(because: "an unconfirmed completed entry expires after 10s");
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
        });

        // act
        queue.Enqueue(new TestCommand("a", "1"));
        queue.Enqueue(new TestCommand("a", "2"));
        queue.Enqueue(new TestCommand("a", "3"));

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
        });

        // act
        queue.Enqueue(new CoalescingTestCommand("a", "1"));
        queue.Enqueue(new CoalescingTestCommand("a", "2"));
        queue.Enqueue(new CoalescingTestCommand("a", "3"));
        gate.SetResult(default);

        // assert
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
        });

        // act
        queue.Enqueue(new TestCommand("a"));
        queue.Enqueue(new TestCommand("b"));
        queue.Enqueue(new TestCommand(""));
        queue.Enqueue(new TestCommand(""));
        queue.Enqueue(new TestCommand(null!));

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
            return Task.CompletedTask;
        });
        queue.Pause();

        // act
        queue.Enqueue(new TestCommand("a"));
        queue.Enqueue(new TestCommand("a"));
        queue.Enqueue(new TestCommand("b"));

        // assert
        queue.IsPaused.Should().BeTrue(because: "Pause takes effect at once");
        runCount.Should().Be(0, because: "a paused queue behaves like a lost connection");
        queue.GetEntries().Should().OnlyContain(x => x.Stage == QueuedCommandStage.Waiting,
            because: "commands are held before their first attempt");

        queue.Resume();
        await TestWait.WhenPolled(() => runCount.Should().Be(3, because: "resuming releases the whole backlog"));
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
        });

        // act
        queue.Enqueue(new TestCommand("a"));
        await hasStarted.Task;
        queue.Pause();
        serverReply.SetResult(default);

        // assert
        await TestWait.WhenPolled(() => queue.GetEntries("a").Single().Stage
            .Should().Be(QueuedCommandStage.Completed, because: "an attempt already in flight is left to finish"));
    }

    [Fact]
    public async Task DisposeShouldStopRetryingWithoutReportingFailure()
    {
        // arrange
        var tryCount = 0;
        var reported = new List<Exception>();
        var queue = new CommandQueueUI((_, _) => {
            Interlocked.Increment(ref tryCount);
            throw new TimeoutException();
        }, failureReporter: reported.Add) { RetryDelay = TimeSpan.FromMilliseconds(10) };
        queue.Enqueue(new TestCommand("a"));
        await TestWait.WhenPolled(() => tryCount.Should().BeGreaterThan(1));

        // act
        queue.Dispose();
        var stoppedAt = tryCount;
        await Task.Delay(100);

        // assert
        tryCount.Should().BeLessThanOrEqualTo(stoppedAt + 1, because: "a disposed queue stops retrying");
        reported.Should().BeEmpty(because: "a stop isn't a failure the user has to hear about");
    }

    // Nested types

    private sealed record TestCommand(string PartitionKey, string Name = "") : IQueuedCommand;

    private sealed record CoalescingTestCommand(string PartitionKey, string Name) : IQueuedCommand
    {
        public QueuedCommandCoalescing Coalescing => QueuedCommandCoalescing.ReplaceWaiting;
    }
}
