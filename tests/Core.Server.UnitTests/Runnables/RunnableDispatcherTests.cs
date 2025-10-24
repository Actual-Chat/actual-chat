namespace ActualChat.Core.Server.UnitTests.Runnables;

public class RunnableDispatcherTests
{
    private static readonly TimeSpan DefaultWaitTime = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Add_Runnable_Starts_On_All_Runners()
    {
        var scheduler = new RunnableDispatcher();
        var runner1 = new RunnableRunner();
        var runner2 = new RunnableRunner();
        scheduler.Add(runner1).Should().BeTrue();
        scheduler.Add(runner1).Should().BeFalse();
        scheduler.Add(runner2).Should().BeTrue();
        scheduler.Add(runner2).Should().BeFalse();

        var startedCts = TaskCompletionSourceExt.New();
        var runnable = MakeRunnable(startedCts);
        scheduler.Add(runnable).Should().BeTrue();
        runner1.StartedRunnables.Should().ContainKey(runnable);
        runner2.StartedRunnables.Should().ContainKey(runnable);

        await startedCts.Task.WaitAsync(DefaultWaitTime);
        await scheduler.DisposeAsync().AsTask();

        var startedRunnables = scheduler.Runners.SelectMany(x => x.StartedRunnables.Values).ToList();
        startedRunnables.Count.Should().Be(2);
        startedRunnables.All(x => x.Task.IsCanceledOrFaultedWithOce()).Should().BeTrue();
    }

    [Fact]
    public async Task Add_Runner_Starts_All_Runnables()
    {
        var scheduler = new RunnableDispatcher();

        var r1StartedTcs = TaskCompletionSourceExt.New();
        var r1 = MakeRunnable(r1StartedTcs);
        scheduler.Add(r1).Should().BeTrue();
        scheduler.Add(r1).Should().BeFalse();

        var r2StartedTcs = TaskCompletionSourceExt.New();
        var r2 = MakeRunnable(r2StartedTcs);
        scheduler.Add(r2).Should().BeTrue();
        scheduler.Add(r2).Should().BeFalse();

        var runner = new RunnableRunner();
        scheduler.Add(runner).Should().BeTrue();
        runner.StartedRunnables.Should().ContainKey(r1);
        runner.StartedRunnables.Should().ContainKey(r2);

        await Task.WhenAll(r1StartedTcs.Task, r2StartedTcs.Task).WaitAsync(DefaultWaitTime);
        await scheduler.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task Remove_Runnable_Stops_On_All_Runners()
    {
        var scheduler = new RunnableDispatcher();

        var runner1 = new RunnableRunner();
        scheduler.Add(runner1).Should().BeTrue();
        scheduler.Add(runner1).Should().BeFalse();

        var runner2 = new RunnableRunner();
        scheduler.Add(runner2).Should().BeTrue();
        scheduler.Add(runner2).Should().BeFalse();

        var startedTcs = TaskCompletionSourceExt.New();
        var runnable = MakeRunnable(startedTcs);
        scheduler.Add(runnable).Should().BeTrue();
        scheduler.Add(runnable).Should().BeFalse();
        runner1.StartedRunnables.Should().ContainKey(runnable);
        runner2.StartedRunnables.Should().ContainKey(runnable);

        await startedTcs.Task.WaitAsync(DefaultWaitTime);
        runner1.StartedRunnables.Should().ContainKey(runnable);
        runner2.StartedRunnables.Should().ContainKey(runnable);

        await scheduler.Remove(runnable, mustStop: true);
        runner1.StartedRunnables.Should().NotContainKey(runnable);
        runner2.StartedRunnables.Should().NotContainKey(runnable);

        await scheduler.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task Remove_Runner_Disposes_It_When_mustStop()
    {
        var scheduler = new RunnableDispatcher();
        var runner = new RunnableRunner();
        scheduler.Add(runner).Should().BeTrue();

        var startedTcs = TaskCompletionSourceExt.New();
        var runnable = MakeRunnable(startedTcs);
        scheduler.Add(runnable).Should().BeTrue();

        await startedTcs.Task.WaitAsync(DefaultWaitTime);
        await scheduler.Remove(runner, mustStop: true);

        foreach (var x in runner.StartedRunnables.Values)
            x.Task.IsCompleted.Should().BeTrue();

        await scheduler.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task Remove_Runner_Without_Stop_Leaves_It_Running()
    {
        var scheduler = new RunnableDispatcher();
        var runner = new RunnableRunner();
        scheduler.Add(runner).Should().BeTrue();

        var r1StartedTcs = TaskCompletionSourceExt.New();
        var r1 = MakeRunnable(r1StartedTcs);
        scheduler.Add(r1).Should().BeTrue();
        await r1StartedTcs.Task.WaitAsync(DefaultWaitTime);
        await scheduler.Remove(runner, mustStop: false);
        runner.StartedRunnables.Should().ContainKey(r1);
        runner.StartedRunnables.Values.All(x => !x.Task.IsCompleted).Should().BeTrue();

        var r2StartedTcs = TaskCompletionSourceExt.New();
        var r2 = MakeRunnable(r2StartedTcs);
        scheduler.Add(r2).Should().BeTrue();
        runner.StartedRunnables.Should().NotContainKey(r2);
        runner.StartedRunnables.Values.All(x => !x.Task.IsCompleted).Should().BeTrue();

        await runner.DisposeAsync().AsTask();
        await scheduler.DisposeAsync().AsTask();
    }

    // Private methods

    private static IRunnable MakeRunnable(TaskCompletionSource startedTcs)
        => Runnable.New(async (_, ct) => {
            startedTcs.TrySetResult();
            await TaskExt.NeverEnding(ct);
        });
}
