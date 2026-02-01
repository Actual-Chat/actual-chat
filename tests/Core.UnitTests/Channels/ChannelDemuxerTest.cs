using ActualChat.Async;

namespace ActualChat.Core.UnitTests.Channels;

public class ChannelDemuxerTest
{
    [Fact]
    public async Task Demuxer_ShouldRouteByStreamIndex()
    {
        await using var demuxer = new ChannelDemuxer<TestItem>();

        var input = Channel.CreateUnbounded<TestItem>();
        demuxer.SetInput(input.Reader);

        var channel1 = demuxer.GetOrCreateChannel(1);
        var channel2 = demuxer.GetOrCreateChannel(2);

        await input.Writer.WriteAsync(new TestItem { StreamIndex = 1, Value = "A" });
        await input.Writer.WriteAsync(new TestItem { StreamIndex = 2, Value = "B" });
        await input.Writer.WriteAsync(new TestItem { StreamIndex = 1, Value = "C" });

        // Read from channel 1
        var item1a = await ReadWithTimeout(channel1);
        var item1b = await ReadWithTimeout(channel1);
        Assert.Equal("A", item1a?.Value);
        Assert.Equal("C", item1b?.Value);

        // Read from channel 2
        var item2 = await ReadWithTimeout(channel2);
        Assert.Equal("B", item2?.Value);
    }

    [Fact]
    public async Task Demuxer_ShouldDropItemsForUnknownStreamIndex()
    {
        await using var demuxer = new ChannelDemuxer<TestItem>();

        var input = Channel.CreateUnbounded<TestItem>();
        demuxer.SetInput(input.Reader);

        var channel1 = demuxer.GetOrCreateChannel(1);

        // Write items for stream 1 and unknown stream 99
        await input.Writer.WriteAsync(new TestItem { StreamIndex = 1, Value = "Known" });
        await input.Writer.WriteAsync(new TestItem { StreamIndex = 99, Value = "Unknown" });
        await input.Writer.WriteAsync(new TestItem { StreamIndex = 1, Value = "Known2" });

        var item1 = await ReadWithTimeout(channel1);
        var item2 = await ReadWithTimeout(channel1);

        Assert.Equal("Known", item1?.Value);
        Assert.Equal("Known2", item2?.Value);
    }

    [Fact]
    public async Task GetOrCreateChannel_ShouldReturnSameChannelForSameIndex()
    {
        await using var demuxer = new ChannelDemuxer<TestItem>();

        var channel1a = demuxer.GetOrCreateChannel(1);
        var channel1b = demuxer.GetOrCreateChannel(1);

        Assert.Same(channel1a, channel1b);
    }

    [Fact]
    public async Task TryRemoveChannel_ShouldCompleteTheChannel()
    {
        await using var demuxer = new ChannelDemuxer<TestItem>();

        var input = Channel.CreateUnbounded<TestItem>();
        demuxer.SetInput(input.Reader);

        var channel1 = demuxer.GetOrCreateChannel(1);

        await input.Writer.WriteAsync(new TestItem { StreamIndex = 1, Value = "Before" });
        var item = await ReadWithTimeout(channel1);
        Assert.Equal("Before", item?.Value);

        // Remove channel
        var removed = demuxer.TryRemoveChannel(1);
        Assert.True(removed);

        // Channel should be completed
        await Task.Delay(50);
        Assert.True(channel1.Completion.IsCompleted);
    }

    [Fact]
    public async Task SetInput_ShouldThrowIfCalledTwice()
    {
        await using var demuxer = new ChannelDemuxer<TestItem>();

        var input1 = Channel.CreateUnbounded<TestItem>();
        var input2 = Channel.CreateUnbounded<TestItem>();

        demuxer.SetInput(input1.Reader);

        Assert.Throws<InvalidOperationException>(() => demuxer.SetInput(input2.Reader));
    }

    [Fact]
    public async Task Dispose_ShouldCompleteAllChannels()
    {
        var demuxer = new ChannelDemuxer<TestItem>();

        var input = Channel.CreateUnbounded<TestItem>();
        demuxer.SetInput(input.Reader);

        var channel1 = demuxer.GetOrCreateChannel(1);
        var channel2 = demuxer.GetOrCreateChannel(2);

        await demuxer.DisposeAsync();

        Assert.True(channel1.Completion.IsCompleted);
        Assert.True(channel2.Completion.IsCompleted);
    }

    [Fact]
    public async Task MuxerAndDemuxer_ShouldWorkTogether()
    {
        await using var muxer = new ChannelMuxer<TestItem>();
        await using var demuxer = new ChannelDemuxer<TestItem>();

        // Set up muxer sources
        var source1 = Channel.CreateUnbounded<TestItem>();
        var source2 = Channel.CreateUnbounded<TestItem>();
        var index1 = muxer.AddSource(source1.Reader);
        var index2 = muxer.AddSource(source2.Reader);

        // Connect muxer output to demuxer input
        demuxer.SetInput(muxer.Output);

        // Set up demuxer channels
        var demuxChannel1 = demuxer.GetOrCreateChannel(index1);
        var demuxChannel2 = demuxer.GetOrCreateChannel(index2);

        // Write to sources
        await source1.Writer.WriteAsync(new TestItem { Value = "From1" });
        await source2.Writer.WriteAsync(new TestItem { Value = "From2" });

        // Read from demuxed channels
        var item1 = await ReadWithTimeout(demuxChannel1);
        var item2 = await ReadWithTimeout(demuxChannel2);

        Assert.Equal("From1", item1?.Value);
        Assert.Equal("From2", item2?.Value);
        Assert.Equal(index1, item1?.StreamIndex);
        Assert.Equal(index2, item2?.StreamIndex);
    }

    private static async Task<TestItem?> ReadWithTimeout(ChannelReader<TestItem> reader, int timeoutMs = 1000)
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
