using ActualChat.Audio;
using ActualChat.Live;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class AudioStreamDemuxerTest
{
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");
    private const int HeavyBacklogFrameCount = 500; // 10s at 20ms/frame

    [Fact]
    public async Task NoFrameIsDroppedHoweverFarBehindTheTrackIs()
    {
        // arrange
        var items = new List<MuxedAudioStreamItem> { Start(1) };
        items.AddRange(Frames(1, HeavyBacklogFrameCount));
        items.Add(Start(2));

        // act
        var frames = await RunAndCollect(items, 1);

        // assert
        frames.Count.Should().Be(HeavyBacklogFrameCount);
    }

    [Fact]
    public async Task AnEndedTrackKeepsWhatIsStillQueued()
    {
        // arrange
        var items = new List<MuxedAudioStreamItem> { Start(1) };
        items.AddRange(Frames(1, HeavyBacklogFrameCount));
        items.Add(new MuxedAudioStreamEnd { StreamIndex = 1 });
        items.Add(Start(2));

        // act
        var frames = await RunAndCollect(items, 1);

        // assert
        frames.Count.Should().Be(HeavyBacklogFrameCount);
    }

    [Fact]
    public async Task ConcurrentTracksAreIndependent()
    {
        // arrange
        var items = new List<MuxedAudioStreamItem> { Start(1) };
        items.AddRange(Frames(1, 50));
        items.Add(Start(2));
        items.AddRange(Frames(2, 30));

        // act
        var first = await RunAndCollect(items, 1);
        var second = await RunAndCollect(items, 2);

        // assert
        first.Count.Should().Be(50);
        second.Count.Should().Be(30);
    }

    [Fact]
    public async Task FramesArriveInOrderWithTheirOffsets()
    {
        // arrange
        var items = new List<MuxedAudioStreamItem> { Start(1) };
        items.AddRange(Frames(1, 5));

        // act
        var frames = await RunAndCollect(items, 1);

        // assert
        frames.Select(f => f.Offset).Should().Equal(
            Enumerable.Range(0, 5).Select(i => Constants.Audio.OpusFrameDuration * i));
    }

    [Fact]
    public async Task ArrivalLagShouldBeAskedForEachStreamAsItIsReported()
    {
        // arrange
        var items = new List<MuxedAudioStreamItem> { Start(1), Start(2) };
        items.AddRange(Frames(1, 3));
        items.Add(new MuxedAudioStreamEnd { StreamIndex = 1 });
        items.Add(new MuxedAudioStreamReset());
        var askedStreamIndexes = new List<int>();

        // act
        await using var demuxer = new AudioStreamDemuxer(
            items.ToAsyncEnumerable(), NullLogger.Instance) {
            ArrivalLagProvider = streamIndex => {
                askedStreamIndexes.Add(streamIndex);
                return null;
            },
        };
        await demuxer.Run();

        // assert
        askedStreamIndexes.Should().Equal([1, 2],
            "an ended stream is reported on its end item, one still open on the reset that flushes it");
    }

    [Fact]
    public void ArrivalLagShouldKeepTheFirstTheWorstAndTheLastFrame()
    {
        // arrange
        var lag = new AudioStreamDemuxer.ArrivalLag(
            TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));

        // act
        lag = lag.Add(TimeSpan.FromMilliseconds(900)).Add(TimeSpan.FromMilliseconds(300));

        // assert
        lag.Should().Be(new AudioStreamDemuxer.ArrivalLag(
            TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(900), TimeSpan.FromMilliseconds(300)));
    }

    // Private methods

    private static async Task<List<AudioFrame>> RunAndCollect(
        IReadOnlyList<MuxedAudioStreamItem> items,
        int streamIndex)
    {
        IAsyncEnumerable<AudioFrame>? tracked = null;
        await using var demuxer = new AudioStreamDemuxer(items.ToAsyncEnumerable(), null);
        demuxer.StreamStarted += (info, _, frames) => {
            if (Equals(info.StreamId, StreamIdOf(streamIndex)))
                tracked = frames;
        };
        await demuxer.Run();
        return tracked == null
            ? []
            : await tracked.ToListAsync();
    }

    private static MuxedAudioStreamStart Start(int index)
        => new() {
            StreamIndex = index,
            StreamInfo = new LiveAudioStreamInfo {
                ChatId = TestChatId,
                AuthorId = AuthorId.New(TestChatId, index),
                StreamId = StreamIdOf(index),
                BeginsAt = Moment.EpochStart,
            },
        };

    private static IEnumerable<MuxedAudioStreamItem> Frames(int index, int count)
        => Enumerable.Range(0, count).Select(i => new MuxedAudioFrame {
            StreamIndex = index,
            Data = new byte[] { (byte)i },
            Offset = Constants.Audio.OpusFrameDuration * i,
        });

    private static string StreamIdOf(int index)
        => index.ToString();
}
