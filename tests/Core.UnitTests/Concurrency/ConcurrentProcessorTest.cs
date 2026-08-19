using ActualChat.Concurrency;

namespace ActualChat.Core.UnitTests.Concurrency;

public class ConcurrentProcessorTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Theory]
    [InlineData(1, 2, true)]
    [InlineData(1, 2, false)]
    [InlineData(3, 10, true)]
    [InlineData(3, 10, false)]
    [InlineData(10, 20, true)]
    [InlineData(10, 20, false)]
    public async Task ShouldRemove(int concurrencyLevel, int itemCount, bool cancel)
    {
        // arrange
        var ids = Enumerable.Range(1, itemCount).Select(i => i.ToString()).ToArray();
        var fetcher = new Fetcher().Register(ids);

        await using var sut = new ConcurrentProcessor<string, string>(
            concurrencyLevel, fetcher.Fetch,
            log: Out.ToLogger<ConcurrentProcessor<string, string>>());

        // act - enqueue all
        var items = ids.Select(id => sut.Enqueue(id)).ToList();

        // assert - all items enqueued, first batch started
        await TestExt.When(() => {
            sut.QueueSize.Should().Be(itemCount);
            sut.Queue.Count(x => x.IsStarted).Should().Be(concurrencyLevel);
            sut.Queue.Count(x => !x.IsStarted).Should().Be(itemCount - concurrencyLevel);
        }, TimeSpan.FromSeconds(5));

        // act - remove all
        sut.RemoveMany(cancel, ids);

        // assert - a started item we didn't cancel still holds its slot, so it stays queued
        sut.QueueSize.Should().Be(cancel ? 0 : concurrencyLevel);

        // not-started items are always cancelled on remove
        await TestExt.When(() => {
            items.Skip(concurrencyLevel).Should().AllSatisfy(
                x => x.ResultTask.IsCanceled.Should().BeTrue());
        }, TimeSpan.FromSeconds(5));

        if (cancel) {
            // started items should be cancelled too
            await TestExt.When(() => {
                items.Take(concurrencyLevel).Should().AllSatisfy(
                    x => x.ResultTask.IsCanceled.Should().BeTrue());
            }, TimeSpan.FromSeconds(5));
        }
        else {
            // started items continue running - resolve them
            fetcher.SetDefaultResults(ids);
            await TestExt.When(() => {
                items.Take(concurrencyLevel).Should().AllSatisfy(
                    x => x.ResultTask.IsCompletedSuccessfully.Should().BeTrue());
                sut.QueueSize.Should().Be(0);
            }, TimeSpan.FromSeconds(5));
        }
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(3, 10)]
    [InlineData(10, 20)]
    public async Task ShouldProcessToCompletion(int concurrencyLevel, int itemCount)
    {
        // arrange
        var ids = Enumerable.Range(1, itemCount).Select(i => i.ToString()).ToArray();
        var fetcher = new Fetcher().Register(ids);

        await using var sut = new ConcurrentProcessor<string, string>(
            concurrencyLevel, fetcher.Fetch,
            log: Out.ToLogger<ConcurrentProcessor<string, string>>());

        // act
        var items = ids.Select(id => sut.Enqueue(id)).ToList();

        // assert - all items enqueued
        await TestExt.When(() => {
            sut.QueueSize.Should().Be(itemCount);
            sut.Queue.Count(x => x.IsStarted).Should().Be(concurrencyLevel);
        }, TimeSpan.FromSeconds(5));

        // act - complete all
        fetcher.SetDefaultResults(ids);

        // assert - all drained
        await TestExt.When(() => {
            sut.QueueSize.Should().Be(0);
        }, TimeSpan.FromSeconds(5));
        items.Should().AllSatisfy(x => x.ResultTask.IsCompletedSuccessfully.Should().BeTrue());
    }

    [Fact]
    public async Task ShouldDeduplicateByKey()
    {
        // arrange
        var fetcher = new Fetcher().Register(["a"]);
        await using var sut = new ConcurrentProcessor<string, string>(
            1, fetcher.Fetch,
            log: Out.ToLogger<ConcurrentProcessor<string, string>>());

        // act
        var item1 = sut.Enqueue("a");
        var item2 = sut.Enqueue("a");

        // assert
        ReferenceEquals(item1, item2).Should().BeTrue();
        sut.QueueSize.Should().Be(1);

        fetcher.SetResult("a");
        await TestExt.When(() => sut.QueueSize.Should().Be(0), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ShouldRespectConcurrencyLevel()
    {
        // arrange
        var concurrencyLevel = 2;
        var ids = new[] { "a", "b", "c", "d" };
        var fetcher = new Fetcher().Register(ids);
        await using var sut = new ConcurrentProcessor<string, string>(
            concurrencyLevel, fetcher.Fetch,
            log: Out.ToLogger<ConcurrentProcessor<string, string>>());

        // act
        foreach (var id in ids)
            sut.Enqueue(id);

        // assert - only concurrencyLevel items should be started
        await TestExt.When(() => {
            sut.Queue.Count(x => x.IsStarted).Should().Be(concurrencyLevel);
            sut.Queue.Count(x => !x.IsStarted).Should().Be(ids.Length - concurrencyLevel);
        }, TimeSpan.FromSeconds(5));

        // act - complete the first batch
        fetcher.SetResult("a");
        fetcher.SetResult("b");

        // assert - next items should start
        await TestExt.When(() => {
            sut.Queue.Count(x => x.IsStarted).Should().Be(concurrencyLevel);
            sut.Queue.Where(x => x.IsStarted).Select(x => x.Key).Should().BeEquivalentTo("c", "d");
        }, TimeSpan.FromSeconds(5));

        fetcher.SetResult("c");
        fetcher.SetResult("d");
        await TestExt.When(() => sut.QueueSize.Should().Be(0), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ShouldPropagateResults()
    {
        // arrange
        var fetcher = new Fetcher().Register(["x"]);
        await using var sut = new ConcurrentProcessor<string, string>(
            1, fetcher.Fetch,
            log: Out.ToLogger<ConcurrentProcessor<string, string>>());

        // act
        var item = sut.Enqueue("x");
        fetcher.SetResult("x", "hello");

        // assert
        var result = await item.ResultTask.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().Be("hello");
    }

    [Fact]
    public async Task ShouldPropagateExceptions()
    {
        // arrange
        var error = new InvalidOperationException("test error");
        await using var sut = new ConcurrentProcessor<string, string>(1,
            (_, _) => Task.FromException<string>(error),
            log: Out.ToLogger<ConcurrentProcessor<string, string>>());

        // act
        var item = sut.Enqueue("x");

        // assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => item.ResultTask.WaitAsync(TimeSpan.FromSeconds(5)));
        ex.Should().BeSameAs(error);
        await TestExt.When(() => sut.QueueSize.Should().Be(0), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ShouldCancelViaRemove()
    {
        // arrange
        var fetcher = new Fetcher().Register(["a"]);
        await using var sut = new ConcurrentProcessor<string, string>(
            1, fetcher.Fetch,
            log: Out.ToLogger<ConcurrentProcessor<string, string>>());

        var item = sut.Enqueue("a");
        await TestExt.When(() => item.IsStarted.Should().BeTrue(), TimeSpan.FromSeconds(5));

        // act
        sut.Remove("a", true);

        // assert
        sut.QueueSize.Should().Be(0);
        await TestExt.When(() => item.ResultTask.IsCanceled.Should().BeTrue(), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ShouldKeepUncancelledRunningItemQueued()
    {
        // arrange
        var fetcher = new Fetcher().Register(["a"]);
        await using var sut = new ConcurrentProcessor<string, string>(
            1, fetcher.Fetch,
            log: Out.ToLogger<ConcurrentProcessor<string, string>>());

        var item = sut.Enqueue("a");
        await TestExt.When(() => item.IsStarted.Should().BeTrue(), TimeSpan.FromSeconds(5));

        // act
        sut.Remove("a", false);

        // assert - it still holds a slot, so it stays queued and Enqueue must not duplicate it
        sut.QueueSize.Should().Be(1);
        ReferenceEquals(sut.Enqueue("a"), item).Should().BeTrue();

        fetcher.SetResult("a");
        await TestExt.When(() => sut.QueueSize.Should().Be(0), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ShouldTimeOutProcessCall()
    {
        // arrange
        var fetcher = new Fetcher().Register(["a", "b"]);
        await using var sut = new ConcurrentProcessor<string, string>(
            1, fetcher.Fetch, TimeSpan.FromMilliseconds(200),
            log: Out.ToLogger<ConcurrentProcessor<string, string>>());

        // act
        var item = sut.Enqueue("a");
        var next = sut.Enqueue("b");

        // assert - the timed out item frees its slot, so the next one gets to start
        await Assert.ThrowsAsync<TimeoutException>(() => item.ResultTask.WaitAsync(TimeSpan.FromSeconds(5)));
        await TestExt.When(() => next.IsStarted.Should().BeTrue(), TimeSpan.FromSeconds(5));

        fetcher.SetResult("b");
        await TestExt.When(() => sut.QueueSize.Should().Be(0), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ShouldReturnNullForMissingGet()
    {
        using var sut = new ConcurrentProcessor<string, string>(1, (_, _) => Task.FromResult(""), mustStart: false);
        sut.Get("nonexistent").Should().BeNull();
    }

    [Fact]
    public async Task ShouldGetExistingItem()
    {
        // arrange
        var fetcher = new Fetcher().Register(["a"]);
        await using var sut = new ConcurrentProcessor<string, string>(
            1, fetcher.Fetch,
            log: Out.ToLogger<ConcurrentProcessor<string, string>>());

        // act
        var enqueued = sut.Enqueue("a");
        var found = sut.Get("a");

        // assert
        found.Should().NotBeNull();
        ReferenceEquals(found, enqueued).Should().BeTrue();

        fetcher.SetResult("a");
        await TestExt.When(() => sut.QueueSize.Should().Be(0), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ShouldTrackEnqueueAndProcessedCounts()
    {
        // arrange
        var fetcher = new Fetcher().Register(["a", "b"]);
        await using var sut = new ConcurrentProcessor<string, string>(
            2, fetcher.Fetch,
            log: Out.ToLogger<ConcurrentProcessor<string, string>>());

        // act
        sut.Enqueue("a");
        sut.Enqueue("b");

        // assert
        sut.EnqueueCount.Should().Be(2);
        sut.ProcessedCount.Should().Be(0);

        fetcher.SetDefaultResults(["a", "b"]);
        await TestExt.When(() => {
            sut.ProcessedCount.Should().Be(2);
            sut.QueueSize.Should().Be(0);
        }, TimeSpan.FromSeconds(5));
    }

    // Nested types

    private sealed class Fetcher
    {
        private readonly Dictionary<string, TaskCompletionSource<string>> _taskSources = new();

        public void Cancel(string id)
            => _taskSources[id].TrySetCanceled();

        public void SetResult(string id, string? data = null)
            => _taskSources[id].TrySetResult(data ?? $"Data for #{id}");

        public void SetDefaultResults(IEnumerable<string> ids)
        {
            foreach (var id in ids)
                SetResult(id);
        }

        public Fetcher Register(params IEnumerable<string> ids)
        {
            foreach (var id in ids)
                _taskSources.GetOrAdd(id);
            return this;
        }

        public Task<string> Fetch(string id, CancellationToken cancellationToken)
            => _taskSources[id].Task.WaitAsync(cancellationToken);
    }
}
