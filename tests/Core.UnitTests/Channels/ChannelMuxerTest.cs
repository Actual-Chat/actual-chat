using ActualChat.Async;

namespace ActualChat.Core.UnitTests.Channels;

public class ChannelMuxerTest
{
    [Fact]
    public async Task AddSource_ShouldAssignUniqueStreamIndex()
    {
        await using var muxer = new ChannelMuxer<TestItem>();

        var source1 = Channel.CreateUnbounded<TestItem>();
        var source2 = Channel.CreateUnbounded<TestItem>();

        var index1 = muxer.AddSource(source1.Reader);
        var index2 = muxer.AddSource(source2.Reader);

        Assert.NotEqual(index1, index2);
        Assert.Equal(1, index1);
        Assert.Equal(2, index2);
    }

    [Fact]
    public async Task Muxer_ShouldSetStreamIndexOnItems()
    {
        await using var muxer = new ChannelMuxer<TestItem>();

        var source1 = Channel.CreateUnbounded<TestItem>();
        var source2 = Channel.CreateUnbounded<TestItem>();

        var index1 = muxer.AddSource(source1.Reader);
        var index2 = muxer.AddSource(source2.Reader);

        await source1.Writer.WriteAsync(new TestItem { Value = "A" });
        await source2.Writer.WriteAsync(new TestItem { Value = "B" });
        source1.Writer.TryComplete();
        source2.Writer.TryComplete();

        var items = new List<TestItem>();
        while (items.Count < 2) {
            var item = await ReadWithTimeout(muxer.Output);
            if (item == null)
                break;
            items.Add(item);
        }

        Assert.Equal(2, items.Count);
        var itemA = items.Single(x => x.Value == "A");
        var itemB = items.Single(x => x.Value == "B");
        Assert.Equal(index1, itemA.StreamIndex);
        Assert.Equal(index2, itemB.StreamIndex);
    }

    [Fact]
    public async Task Muxer_ShouldMergeMultipleSources()
    {
        await using var muxer = new ChannelMuxer<TestItem>();

        var source1 = Channel.CreateUnbounded<TestItem>();
        var source2 = Channel.CreateUnbounded<TestItem>();

        muxer.AddSource(source1.Reader);
        muxer.AddSource(source2.Reader);

        for (var i = 0; i < 5; i++) {
            await source1.Writer.WriteAsync(new TestItem { Value = $"S1-{i}" });
            await source2.Writer.WriteAsync(new TestItem { Value = $"S2-{i}" });
        }
        source1.Writer.TryComplete();
        source2.Writer.TryComplete();

        var items = new List<TestItem>();
        while (items.Count < 10) {
            var item = await ReadWithTimeout(muxer.Output);
            if (item == null)
                break;
            items.Add(item);
        }

        Assert.Equal(10, items.Count);
        Assert.Equal(5, items.Count(x => x.Value.StartsWith("S1-")));
        Assert.Equal(5, items.Count(x => x.Value.StartsWith("S2-")));
    }

    [Fact]
    public async Task RemoveSource_ShouldRemoveFromTracking()
    {
        await using var muxer = new ChannelMuxer<TestItem>();

        var source = Channel.CreateUnbounded<TestItem>();
        var index = muxer.AddSource(source.Reader);

        // RemoveSource removes tracking but pump continues until source completes
        var removed = muxer.RemoveSource(index);
        Assert.True(removed);

        // Removing again should return false
        removed = muxer.RemoveSource(index);
        Assert.False(removed);
    }

    [Fact]
    public async Task Dispose_ShouldCompleteOutput()
    {
        var muxer = new ChannelMuxer<TestItem>();
        var source = Channel.CreateUnbounded<TestItem>();
        muxer.AddSource(source.Reader);

        await muxer.DisposeAsync();

        // Output should be completed
        Assert.True(muxer.Output.Completion.IsCompleted);
    }

    private static async Task<TestItem?> ReadWithTimeout(ChannelReader<TestItem> reader, int timeoutMs = 5000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        try {
            return await reader.ReadAsync(cts.Token);
        }
        catch (OperationCanceledException) {
            return null;
        }
    }

    private class TestItem : IMuxable
    {
        public int StreamIndex { get; set; }
        public string Value { get; init; } = "";
    }
}
