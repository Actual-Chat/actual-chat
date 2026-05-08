using ActualChat.Streaming.Services;

namespace ActualChat.Streaming.UnitTests;

public class StreamCacheFetchDeduperTest
{
    [Fact(Timeout = 5_000)]
    public async Task ConcurrentFetches_ShareSingleInvocation()
    {
        // arrange
        var inflight = new ConcurrentDictionary<StreamId, Task<bool>>();
        var streamId = NewStreamId();
        var fetchCount = 0;
        var gate = TaskCompletionSourceExt.New<bool>();

        Task<bool> Fetch(StreamId _) {
            Interlocked.Increment(ref fetchCount);
            return gate.Task;
        }

        // act: two callers race for the same streamId.
        var caller1 = StreamCacheFetchDeduper.Run(inflight, streamId, Fetch);
        var caller2 = StreamCacheFetchDeduper.Run(inflight, streamId, Fetch);
        // Yield so caller1 has a chance to register the in-flight entry first.
        await Task.Yield();
        gate.SetResult(true);
        var results = await Task.WhenAll(caller1, caller2);

        // assert
        fetchCount.Should().Be(1, "second caller must observe the in-flight task");
        results.Should().Equal(true, true);
        inflight.Should().BeEmpty("entry is removed after the fetch completes");
    }

    [Fact(Timeout = 5_000)]
    public async Task FetchFalseResult_BothCallersSeeFalse()
    {
        // arrange
        var inflight = new ConcurrentDictionary<StreamId, Task<bool>>();
        var streamId = NewStreamId();

        // act
        var c1 = StreamCacheFetchDeduper.Run(inflight, streamId, _ => Task.FromResult(false));
        var c2 = StreamCacheFetchDeduper.Run(inflight, streamId, _ => Task.FromResult(false));
        var results = await Task.WhenAll(c1, c2);

        // assert
        results.Should().Equal(false, false);
    }

    [Fact(Timeout = 5_000)]
    public async Task FetchThrows_BothCallersObserveTheException()
    {
        // arrange
        var inflight = new ConcurrentDictionary<StreamId, Task<bool>>();
        var streamId = NewStreamId();
        var gate = TaskCompletionSourceExt.New<bool>();
        Task<bool> Fetch(StreamId _) => Throws();

        async Task<bool> Throws() {
            await gate.Task;
            throw new InvalidOperationException("fetch failed");
        }

        // act
        var c1 = StreamCacheFetchDeduper.Run(inflight, streamId, Fetch);
        var c2 = StreamCacheFetchDeduper.Run(inflight, streamId, Fetch);
        await Task.Yield();
        gate.SetResult(true);

        // assert
        await FluentActions.Awaiting(() => c1).Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => c2).Should().ThrowAsync<InvalidOperationException>();
        inflight.Should().BeEmpty();
    }

    [Fact(Timeout = 5_000)]
    public async Task SequentialFetches_AreNotCoalesced()
    {
        // arrange
        var inflight = new ConcurrentDictionary<StreamId, Task<bool>>();
        var streamId = NewStreamId();
        var fetchCount = 0;
        Task<bool> Fetch(StreamId _) {
            Interlocked.Increment(ref fetchCount);
            return Task.FromResult(true);
        }

        // act
        await StreamCacheFetchDeduper.Run(inflight, streamId, Fetch);
        await StreamCacheFetchDeduper.Run(inflight, streamId, Fetch);

        // assert
        fetchCount.Should().Be(2, "second call after the first completed must run its own fetch");
    }

    private static StreamId NewStreamId()
        => StreamId.New(new NodeRef(Generate.Option));
}
