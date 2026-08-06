namespace ActualChat.Core.UnitTests.Channels;

public class AsyncObservableTest
{
    [Fact]
    public async Task Subscribe_ShouldReceivePublishedValues()
    {
        var observable = new AsyncObservable<int>();

        await using var subscription = observable.Subscribe();

        observable.Publish(1);
        observable.Publish(2);
        observable.Publish(3);
        observable.TryComplete();

        var values = new List<int>();
        await foreach (var value in subscription.Reader.ReadAllAsync())
            values.Add(value);

        Assert.Equal([1, 2, 3], values);
    }

    [Fact]
    public async Task Subscribe_MultipleSubscribers_ShouldAllReceiveValues()
    {
        var observable = new AsyncObservable<int>();

        await using var sub1 = observable.Subscribe();
        await using var sub2 = observable.Subscribe();
        await using var sub3 = observable.Subscribe();

        observable.Publish(42);
        observable.TryComplete();

        var values1 = new List<int>();
        var values2 = new List<int>();
        var values3 = new List<int>();

        await foreach (var v in sub1.Reader.ReadAllAsync())
            values1.Add(v);
        await foreach (var v in sub2.Reader.ReadAllAsync())
            values2.Add(v);
        await foreach (var v in sub3.Reader.ReadAllAsync())
            values3.Add(v);

        Assert.Equal([42], values1);
        Assert.Equal([42], values2);
        Assert.Equal([42], values3);
    }

    [Fact]
    public async Task Subscribe_AfterComplete_ShouldReturnCompletedSubscription()
    {
        var observable = new AsyncObservable<int>();
        observable.Publish(1);
        observable.TryComplete();

        await using var subscription = observable.Subscribe();

        var values = new List<int>();
        await foreach (var value in subscription.Reader.ReadAllAsync())
            values.Add(value);

        Assert.Empty(values);
        Assert.True(observable.IsCompleted);
    }

    [Fact]
    public async Task Subscribe_AfterCompleteWithError_ShouldPropagateError()
    {
        var observable = new AsyncObservable<int>();
        var expectedException = new InvalidOperationException("Test error");
        observable.TryComplete(expectedException);

        await using var subscription = observable.Subscribe();

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            await foreach (var _ in subscription.Reader.ReadAllAsync()) { }
        });
    }

    [Fact]
    public async Task TryComplete_ShouldCompleteAllSubscribers()
    {
        var observable = new AsyncObservable<int>();

        await using var sub1 = observable.Subscribe();
        await using var sub2 = observable.Subscribe();

        observable.Publish(1);
        observable.TryComplete();

        // Both subscriptions should complete
        var task1 = Task.Run(async () => {
            var count = 0;
            await foreach (var _ in sub1.Reader.ReadAllAsync())
                count++;
            return count;
        });

        var task2 = Task.Run(async () => {
            var count = 0;
            await foreach (var _ in sub2.Reader.ReadAllAsync())
                count++;
            return count;
        });

        var counts = await Task.WhenAll(task1, task2);
        Assert.Equal(1, counts[0]);
        Assert.Equal(1, counts[1]);
    }

    [Fact]
    public async Task TryComplete_CalledTwice_ShouldReturnFalse()
    {
        var observable = new AsyncObservable<int>();

        Assert.True(observable.TryComplete());
        Assert.False(observable.TryComplete());
    }

    [Fact]
    public async Task Publish_AfterComplete_ShouldBeIgnored()
    {
        var observable = new AsyncObservable<int>();

        await using var subscription = observable.Subscribe();

        observable.Publish(1);
        observable.TryComplete();
        observable.Publish(2); // Should be ignored

        var values = new List<int>();
        await foreach (var value in subscription.Reader.ReadAllAsync())
            values.Add(value);

        Assert.Equal([1], values);
    }

    [Fact]
    public async Task Dispose_ShouldUnsubscribe()
    {
        var observable = new AsyncObservable<int>();

        var subscription = observable.Subscribe();
        observable.Publish(1);
        await subscription.DisposeAsync();

        // Publish after dispose - subscription shouldn't receive it
        observable.Publish(2);

        // The channel should be completed after dispose
        var canRead = await subscription.Reader.WaitToReadAsync();
        Assert.True(canRead); // Can read the value published before dispose

        subscription.Reader.TryRead(out var value);
        Assert.Equal(1, value);

        canRead = await subscription.Reader.WaitToReadAsync();
        Assert.False(canRead); // Channel completed after dispose
    }

    [Fact]
    public async Task Subscribe_WithSingleReaderFalse_ShouldAllowMultipleReaders()
    {
        var observable = new AsyncObservable<int>();

        await using var subscription = observable.Subscribe(singleReader: false);

        observable.Publish(1);
        observable.Publish(2);
        observable.TryComplete();

        // Multiple readers should be able to compete for values
        var reader1Task = Task.Run(async () => {
            var values = new List<int>();
            while (await subscription.Reader.WaitToReadAsync())
                if (subscription.Reader.TryRead(out var v))
                    values.Add(v);
            return values;
        });

        var reader2Task = Task.Run(async () => {
            var values = new List<int>();
            while (await subscription.Reader.WaitToReadAsync())
                if (subscription.Reader.TryRead(out var v))
                    values.Add(v);
            return values;
        });

        var results = await Task.WhenAll(reader1Task, reader2Task);
        var allValues = results[0].Concat(results[1]).OrderBy(x => x).ToList();

        // Combined, both readers should have received all values (each value goes to one reader)
        Assert.Equal([1, 2], allValues);
    }

    [Fact]
    public async Task ConcurrentPublishAndSubscribe_ShouldWork()
    {
        var observable = new AsyncObservable<int>();
        var receivedCounts = new ConcurrentBag<int>();
        var subscriberCount = 10;
        var messageCount = 100;

        // Start subscribers
        var subscriberTasks = Enumerable.Range(0, subscriberCount).Select(async subscriberIndex => {
            await using var subscription = observable.Subscribe();
            var count = 0;
            await foreach (var value in subscription.Reader.ReadAllAsync())
                count++;
            receivedCounts.Add(count);
        }).ToList();

        // Give subscribers time to start
        await Task.Delay(50);

        // Publish messages
        for (var i = 0; i < messageCount; i++)
            observable.Publish(i);

        observable.TryComplete();

        await Task.WhenAll(subscriberTasks);

        // Each subscriber should have received all messages published after they subscribed
        Assert.Equal(subscriberCount, receivedCounts.Count);
        Assert.All(receivedCounts, count => Assert.True(count > 0));
    }
}
