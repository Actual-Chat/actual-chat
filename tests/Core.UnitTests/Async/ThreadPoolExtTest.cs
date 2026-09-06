namespace ActualChat.Core.UnitTests.Async;

public sealed class ThreadPoolExtTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public async Task YieldShouldContinueOnThreadPoolOffTheSynchronizationContext()
    {
        // arrange - a dedicated thread with its own context, the shape of the Blazor dispatcher
        var context = new RecordingSynchronizationContext();
        var asyncLocal = new AsyncLocal<int>();
        var resultSource = TaskCompletionSourceExt.New<(bool IsPoolThread, bool HasNoContext, int AsyncLocalValue)>();
        var thread = new Thread(() => {
            SynchronizationContext.SetSynchronizationContext(context);
            asyncLocal.Value = 42;
            _ = Continue();
        });

        // act
        thread.Start();
        var result = await resultSource.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // assert
        result.IsPoolThread.Should().BeTrue();
        result.HasNoContext.Should().BeTrue("the continuation must not run under the context it left");
        result.AsyncLocalValue.Should().Be(42, "the execution context flows, so a compute context survives the hop");
        context.PostCount.Should().Be(0, "nothing may be posted back to the context it left");
        return;

        async Task Continue()
        {
            try {
                await ThreadPoolExt.Yield();
                resultSource.TrySetResult((
                    Thread.CurrentThread.IsThreadPoolThread,
                    SynchronizationContext.Current is null,
                    asyncLocal.Value));
            }
            catch (Exception e) {
                resultSource.TrySetException(e);
            }
        }
    }

    [Fact]
    public async Task YieldShouldNeverCompleteInline()
    {
        // arrange - the starter records when it is done; a continuation that ran inline would see it unfinished
        var isStarterDone = 0;
        var resultSource = TaskCompletionSourceExt.New<bool>();
        var thread = new Thread(() => {
            _ = Continue();
            Volatile.Write(ref isStarterDone, 1);
        });

        // act
        thread.Start();
        var wasStarterDone = await resultSource.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // assert
        wasStarterDone.Should().BeTrue("the starter finishes before a queued continuation gets to run its check");
        return;

        async Task Continue()
        {
            await ThreadPoolExt.Yield();
            // The starter needs only a few instructions after queuing; a spin is what keeps this deterministic
            var spinWait = new SpinWait();
            while (Volatile.Read(ref isStarterDone) == 0 && spinWait.Count < 1000)
                spinWait.SpinOnce();
            resultSource.TrySetResult(Volatile.Read(ref isStarterDone) == 1);
        }
    }

    [Fact]
    public async Task ManyConcurrentYieldsShouldAllComplete()
    {
        // act
        var tasks = Enumerable.Range(0, 1000).Select(async i => {
            await ThreadPoolExt.Yield();
            return i;
        });
        var results = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10));

        // assert
        results.Should().Equal(Enumerable.Range(0, 1000));
    }

    // Nested types

    private sealed class RecordingSynchronizationContext : SynchronizationContext
    {
        private int _postCount;

        public int PostCount => Volatile.Read(ref _postCount);

        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref _postCount);
            base.Post(d, state);
        }
    }
}
