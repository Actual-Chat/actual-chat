using ActualChat.UI.Blazor.App.Services;
using ActualLab.Time.Testing;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class ClientCommandQueueTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public async Task SuccessfulCommandShouldEndUpSettledUntilConfirmed()
    {
        // arrange
        using var clock = new TestClock();
        var queue = new ClientCommandQueue((_, _) => Task.CompletedTask, clock);
        var command = new TestCommand("a");

        // act
        await queue.OnCommand(command, null!, CancellationToken.None);

        // assert
        var entries = queue.GetEntries("a");
        entries.Should().HaveCount(1);
        entries[0].Stage.Should().Be(QueuedCommandStage.Settled,
            because: "the effect survives until a consumer confirms it");

        queue.Confirm(command);
        queue.GetEntries("a").Should().BeEmpty(because: "confirmation drops the entry");
    }

    [Fact]
    public async Task PermanentFailureShouldBeKeptAsFailed()
    {
        // arrange
        using var clock = new TestClock();
        var queue = new ClientCommandQueue((_, _) => throw new InvalidOperationException("nope"), clock);

        // act
        await queue.OnCommand(new TestCommand("a"), null!, CancellationToken.None);

        // assert
        var entry = queue.GetEntries("a").Single();
        entry.Stage.Should().Be(QueuedCommandStage.Failed);
        entry.Error.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task TransientFailureShouldRetryAndCountTries()
    {
        // arrange
        using var clock = new TestClock();
        var tryCount = 0;
        var queue = new ClientCommandQueue((_, _) => {
            tryCount++;
            return tryCount < 3 ? throw new TimeoutException() : Task.CompletedTask;
        }, clock) { RetryDelay = TimeSpan.Zero };

        // act
        await queue.OnCommand(new TestCommand("a"), null!, CancellationToken.None);

        // assert
        tryCount.Should().Be(3, because: "two transient failures are retried");
        queue.GetEntries("a").Single().TryIndex.Should().Be(2, because: "the try index counts the retries");
    }

    [Fact]
    public async Task SettledEntryShouldExpireAfterTtl()
    {
        // arrange
        using var clock = new TestClock();
        var queue = new ClientCommandQueue((_, _) => Task.CompletedTask, clock);
        await queue.OnCommand(new TestCommand("a"), null!, CancellationToken.None);

        // act
        clock.OffsetBy(TimeSpan.FromSeconds(11));

        // assert
        queue.GetEntries("a").Should().BeEmpty(because: "an unconfirmed Settled entry expires after 10s");
    }

    [Fact]
    public async Task NoneCoalescingShouldRunEveryCommand()
    {
        // arrange
        var runCount = 0;
        var gate = TaskCompletionSourceExt.New<Unit>();
        var queue = new ClientCommandQueue(async (_, _) => {
            runCount++;
            await gate.Task.ConfigureAwait(false);
        });

        // act
        var firstTask = queue.OnCommand(new TestCommand("a"), null!, CancellationToken.None);
        await queue.OnCommand(new TestCommand("a"), null!, CancellationToken.None);
        await queue.OnCommand(new TestCommand("a"), null!, CancellationToken.None);
        gate.SetResult(default);
        await firstTask;

        // assert
        runCount.Should().Be(3, because: "a toggle command must never be collapsed");
    }

    [Fact]
    public async Task ReplaceWaitingShouldCollapseTheBacklog()
    {
        // arrange
        var runCount = 0;
        var gate = TaskCompletionSourceExt.New<Unit>();
        var queue = new ClientCommandQueue(async (_, _) => {
            runCount++;
            await gate.Task.ConfigureAwait(false);
        });

        // act
        var firstTask = queue.OnCommand(new CoalescingTestCommand("a"), null!, CancellationToken.None);
        await queue.OnCommand(new CoalescingTestCommand("a"), null!, CancellationToken.None);
        await queue.OnCommand(new CoalescingTestCommand("a"), null!, CancellationToken.None);
        gate.SetResult(default);
        await firstTask;

        // assert
        runCount.Should().Be(2, because: "the two waiting commands collapse into one");
    }

    [Fact]
    public async Task ReDispatchFlagShouldSurviveSuppressedExecutionContext()
    {
        // Commander.Run suppresses the context flow, so an AsyncLocal flag wouldn't survive here

        // arrange
        ClientCommandQueue? queue = null;
        var isRunningFromQueue = false;
        queue = new ClientCommandQueue(async (command, _) => {
            using var _1 = ExecutionContextExt.TrySuppressFlow();
            await Task.Run(() => isRunningFromQueue = queue!.IsRunningFromQueue(command));
        });

        // act
        await queue.OnCommand(new TestCommand("a"), null!, CancellationToken.None);

        // assert
        isRunningFromQueue.Should().BeTrue(
            because: "the filter must recognize a re-dispatch, or it re-queues the command forever");
    }

    [Fact]
    public async Task PausedQueueShouldNotStartTheNextCommand()
    {
        // arrange
        var runCount = 0;
        var queue = new ClientCommandQueue((_, _) => {
            runCount++;
            return Task.CompletedTask;
        });
        queue.Pause();

        // act
        var runTask = queue.OnCommand(new TestCommand("a"), null!, CancellationToken.None);

        // assert
        queue.IsPaused.Should().BeTrue();
        runCount.Should().Be(0, because: "a paused queue behaves like a lost connection");
        queue.GetEntries("a").Single().Stage.Should().Be(QueuedCommandStage.Waiting);

        queue.Resume();
        await runTask;
        runCount.Should().Be(1, because: "resuming releases the command");
    }

    [Fact]
    public async Task ResumeShouldReleaseTheWholeBacklog()
    {
        // arrange
        var runCount = 0;
        var queue = new ClientCommandQueue((_, _) => {
            runCount++;
            return Task.CompletedTask;
        });
        queue.Pause();

        // act
        var firstTask = queue.OnCommand(new TestCommand("a"), null!, CancellationToken.None);
        await queue.OnCommand(new TestCommand("a"), null!, CancellationToken.None);
        await queue.OnCommand(new TestCommand("a"), null!, CancellationToken.None);
        queue.Resume();
        await firstTask;

        // assert
        runCount.Should().Be(3, because: "the backlog drains in order once the queue resumes");
    }

    [Fact]
    public async Task PauseShouldNotInterruptTheRunningCommand()
    {
        // arrange
        var hasStarted = TaskCompletionSourceExt.New<Unit>();
        var serverReply = TaskCompletionSourceExt.New<Unit>();
        var queue = new ClientCommandQueue(async (_, _) => {
            hasStarted.TrySetResult(default);
            await serverReply.Task.ConfigureAwait(false);
        });

        // act
        var runTask = queue.OnCommand(new TestCommand("a"), null!, CancellationToken.None);
        await hasStarted.Task;
        queue.Pause();
        serverReply.SetResult(default);
        await runTask;

        // assert
        queue.GetEntries("a").Single().Stage.Should().Be(QueuedCommandStage.Settled,
            because: "an attempt already in flight is left to finish");
    }

    [Fact]
    public async Task DiRegisteredQueueMustSurviveThePerCommandScope()
    {
        // CommandContext resolves handlers from a DI scope it creates per outermost command,
        // so a queue that isn't shared across scopes hands every re-dispatch a fresh, empty
        // instance - which queues the command again, forever

        // arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var fusion = services.AddFusion();
        fusion.AddClientCommandQueue();
        fusion.Commander.AddHandlers<ClientCommandQueue>();
        services.AddSingleton<TestCommandTarget>();
        fusion.Commander.AddHandlers<TestCommandTarget>();
        await using var c = services.BuildServiceProvider();
        var target = c.GetRequiredService<TestCommandTarget>();

        // act
        await c.Commander()
            .Run(new TestCommand("a"), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));

        // assert
        target.CallCount.Should().Be(1, because: "the queue must run the command exactly once");

        using var scope1 = c.CreateScope();
        using var scope2 = c.CreateScope();
        scope1.ServiceProvider.GetRequiredService<ClientCommandQueue>().Should().BeSameAs(
            scope2.ServiceProvider.GetRequiredService<ClientCommandQueue>(),
            because: "the queue's lane, entries and re-dispatch flag are instance state, "
                + "so a per-scope instance loses all of it between the dispatch and its re-dispatch");
    }

    // Nested types

    private sealed record TestCommand(string PartitionKey) : IQueuedCommand, ICommand<Unit>;

    private sealed class TestCommandTarget : ICommandHandler<TestCommand>
    {
        public int CallCount { get; private set; }
        [CommandHandler]
        public Task OnCommand(TestCommand command, CommandContext context, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed record CoalescingTestCommand(string PartitionKey) : IQueuedCommand
    {
        public QueuedCommandCoalescing Coalescing => QueuedCommandCoalescing.ReplaceWaiting;
    }
}
