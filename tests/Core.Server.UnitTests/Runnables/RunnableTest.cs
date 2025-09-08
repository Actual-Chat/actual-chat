namespace ActualChat.Core.Server.UnitTests.Runnables;

public class RunnableTests
{
    private static readonly TimeSpan DefaultWaitTime = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Runnable_New_Starts_And_Cancels_On_Runner()
    {
        var startedTcs = TaskCompletionSourceExt.New();

        var runnable = Runnable.New(async (_, ct) => {
            startedTcs.TrySetResult();
            await TaskExt.NeverEnding(ct);
        });

        var runner = new RunnableRunner();

        runner.Start(runnable, out var startedRunnable).Should().BeTrue();
        startedRunnable.Should().NotBeNull();
        startedRunnable.RunnableRunner.Should().BeSameAs(runner);
        startedRunnable.Runnable.Should().BeSameAs(runnable);
        runner.StartedRunnables.Should().ContainKey(runnable);
        await startedTcs.Task.WaitAsync(DefaultWaitTime);

        await runner.DisposeAsync().AsTask().WaitAsync(DefaultWaitTime);
        await startedRunnable.Task.SuppressCancellation().WaitAsync(DefaultWaitTime);
        startedRunnable.Task.IsCanceledOrFaultedWithOce().Should().BeTrue();
    }

    [Fact]
    public void Runnable_New_Calls_Delegate()
    {
        var seen = 0;
        var runnable = Runnable.New(async (_, _) => {
            Interlocked.Increment(ref seen);
            await Task.CompletedTask;
        });

        using var runner = new RunnableRunner();
        runner.Start(runnable, out var startedRunnable).Should().BeTrue();
        runner.StartedRunnables.Count.Should().Be(1);

        startedRunnable.Task.Wait(DefaultWaitTime).Should().BeTrue();
        seen.Should().Be(1);

        startedRunnable.Dispose();
        runner.StartedRunnables.Count.Should().Be(0);
    }
}
