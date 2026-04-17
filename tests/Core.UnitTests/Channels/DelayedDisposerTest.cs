namespace ActualChat.Core.UnitTests.Channels;

public class DelayedDisposerTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public async Task ItemsDisposedAfterDelay()
    {
        var delay = TimeSpan.FromMilliseconds(200);
        await using var disposer = new DelayedDisposer<TrackingDisposable>(delay);
        _ = disposer.Run();

        var item = new TrackingDisposable();
        disposer.Add(item);

        // Not disposed immediately
        await Task.Delay(50);
        item.IsDisposed.Should().BeFalse();

        // Disposed after delay
        await Task.Delay(300);
        item.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task MultipleItemsDisposedInOrder()
    {
        var delay = TimeSpan.FromMilliseconds(200);
        await using var disposer = new DelayedDisposer<TrackingDisposable>(delay);
        _ = disposer.Run();

        var items = Enumerable.Range(0, 5).Select(_ => new TrackingDisposable()).ToList();
        foreach (var item in items)
            disposer.Add(item);

        await Task.Delay(50);
        items.Should().AllSatisfy(i => i.IsDisposed.Should().BeFalse());

        await Task.Delay(300);
        items.Should().AllSatisfy(i => i.IsDisposed.Should().BeTrue());
    }

    [Fact]
    public async Task StopDisposesRemainingItemsImmediately()
    {
        var delay = TimeSpan.FromSeconds(60); // Very long — won't expire naturally
        await using var disposer = new DelayedDisposer<TrackingDisposable>(delay);
        _ = disposer.Run();

        var items = Enumerable.Range(0, 3).Select(_ => new TrackingDisposable()).ToList();
        foreach (var item in items)
            disposer.Add(item);

        await Task.Delay(100); // Let worker pick them up
        items.Should().AllSatisfy(i => i.IsDisposed.Should().BeFalse());

        // Stop triggers OnStop → immediate disposal of pending items
        await disposer.Stop();
        items.Should().AllSatisfy(i => i.IsDisposed.Should().BeTrue());
    }

    [Fact]
    public async Task DisposeDisposesRemainingItems()
    {
        var delay = TimeSpan.FromSeconds(60);
        var items = new List<TrackingDisposable>();

        {
            await using var disposer = new DelayedDisposer<TrackingDisposable>(delay);
            _ = disposer.Run();

            items.AddRange(Enumerable.Range(0, 3).Select(_ => new TrackingDisposable()));
            foreach (var item in items)
                disposer.Add(item);

            await Task.Delay(100);
        }
        // After await using exits, items should be disposed
        items.Should().AllSatisfy(i => i.IsDisposed.Should().BeTrue());
    }

    [Fact]
    public async Task StaggeredItemsDisposeAtCorrectTimes()
    {
        var delay = TimeSpan.FromMilliseconds(300);
        await using var disposer = new DelayedDisposer<TrackingDisposable>(delay);
        _ = disposer.Run();

        var first = new TrackingDisposable();
        disposer.Add(first);

        await Task.Delay(200);
        var second = new TrackingDisposable();
        disposer.Add(second);

        // At ~200ms: first not yet expired (needs 300), second just added
        first.IsDisposed.Should().BeFalse();
        second.IsDisposed.Should().BeFalse();

        await Task.Delay(200);
        // At ~400ms: first expired (300ms passed), second not yet (only 200ms)
        first.IsDisposed.Should().BeTrue();
        second.IsDisposed.Should().BeFalse();

        await Task.Delay(200);
        // At ~600ms: both expired
        second.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task DoubleDisposeIsSafe()
    {
        var delay = TimeSpan.FromMilliseconds(100);
        await using var disposer = new DelayedDisposer<TrackingDisposable>(delay);
        _ = disposer.Run();

        var item = new TrackingDisposable();
        disposer.Add(item);

        await Task.Delay(250);
        item.IsDisposed.Should().BeTrue();
        item.DisposeCount.Should().Be(1);

        // Manual dispose after worker already disposed — should not throw
        item.Dispose();
        item.DisposeCount.Should().Be(2); // Called twice but no crash
    }

    // Nested types

    private sealed class TrackingDisposable : IDisposable
    {
        private int _disposeCount;

        public bool IsDisposed => _disposeCount > 0;
        public int DisposeCount => _disposeCount;

        public void Dispose()
            => Interlocked.Increment(ref _disposeCount);
    }
}
