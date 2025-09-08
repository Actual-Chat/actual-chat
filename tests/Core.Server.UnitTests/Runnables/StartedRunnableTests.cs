namespace ActualChat.Core.Server.UnitTests.Runnables;

public class StartedRunnableTests
{
    private static readonly TimeSpan DefaultWaitTime = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Dispose_Cancels_And_Removes_From_Runner()
    {
        var startedTcs = TaskCompletionSourceExt.New();

        var runnable = Runnable.New(async (_, ct) => {
            startedTcs.TrySetResult();
            await TaskExt.NeverEnding(ct);
        });

        var runner = new RunnableRunner();
        runner.Start(runnable, out var startedRunnable).Should().BeTrue();

        await startedTcs.Task.WaitAsync(DefaultWaitTime);

        startedRunnable.Dispose();
        runner.StartedRunnables.ContainsKey(runnable).Should().BeFalse();
        await startedRunnable.Task.SuppressCancellation().WaitAsync(DefaultWaitTime);
        startedRunnable.Task.IsCanceledOrFaultedWithOce().Should().BeTrue();

        await runner.DisposeAsync().AsTask();
    }
}
