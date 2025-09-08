namespace ActualChat.Core.Server.UnitTests.Runnables;

public class RunnableRunnerTest
{
    private static readonly TimeSpan DefaultWaitTime = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Start_Is_Idempotent_Per_Runnable()
    {
        var runnable = Runnable.New(async (_, ct) => {
            await TaskExt.NeverEnding(ct);
        });

        var runner = new RunnableRunner();
        runner.Start(runnable, out var first).Should().BeTrue();
        runner.StartedRunnables.Should().ContainKey(runnable);

        runner.Start(runnable, out var second).Should().BeFalse();
        second.Should().BeSameAs(first);

        await runner.DisposeAsync().AsTask().WaitAsync(DefaultWaitTime);
        first.Task.IsCanceledOrFaultedWithOce().Should().BeTrue();
    }

    [Fact]
    public async Task DisposeAsync_Stops_All_Runnables()
    {
        var counter = StateFactory.Default.NewMutable<int>();
        IRunnable MakeRunnable() => Runnable.New(async (_, ct) => {
            counter.Set(x => x.Value + 1);
            await TaskExt.NeverEnding(ct);
        });

        var runner = new RunnableRunner();
        runner.Start(MakeRunnable(), out var s1).Should().BeTrue();
        runner.Start(MakeRunnable(), out var s2).Should().BeTrue();
        runner.Start(MakeRunnable(), out var s3).Should().BeTrue();

        await counter.Computed.When(x => x == 3).WaitAsync(DefaultWaitTime);
        s1.Task.IsCompleted.Should().BeFalse();
        s2.Task.IsCompleted.Should().BeFalse();
        s3.Task.IsCompleted.Should().BeFalse();

        await runner.DisposeAsync().AsTask().WaitAsync(DefaultWaitTime);
        s1.Task.IsCanceledOrFaultedWithOce().Should().BeTrue();
        s2.Task.IsCanceledOrFaultedWithOce().Should().BeTrue();
        s3.Task.IsCanceledOrFaultedWithOce().Should().BeTrue();
    }
}
