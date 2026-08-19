using ActualChat.Audio;
using ActualChat.Live;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class AudioStreamDemuxerTest
{
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");
    private const int OverBudgetFrameCount = 150; // 3s at 20ms/frame
    private const int WithinBudgetFrameCount = 50; // 1s

    [Fact]
    public async Task ABackloggedTrackIsDroppedWhenTheNextUtteranceStarts()
    {
        // arrange
        var items = new List<MuxedAudioStreamItem> { Start(1) };
        items.AddRange(Frames(1, OverBudgetFrameCount));
        items.Add(Start(2));

        // act
        var frames = await RunAndCollect(items, 1);

        // assert
        frames.Should().BeEmpty();
    }

    [Fact]
    public async Task ATrackWithinBudgetSurvivesTheNextUtterance()
    {
        // arrange
        var items = new List<MuxedAudioStreamItem> { Start(1) };
        items.AddRange(Frames(1, WithinBudgetFrameCount));
        items.Add(Start(2));

        // act
        var frames = await RunAndCollect(items, 1);

        // assert
        frames.Count.Should().Be(WithinBudgetFrameCount);
    }

    [Fact]
    public async Task AnEndedButUndrainedTrackIsStillDropped()
    {
        // arrange
        var items = new List<MuxedAudioStreamItem> { Start(1) };
        items.AddRange(Frames(1, OverBudgetFrameCount));
        items.Add(new MuxedAudioStreamEnd { StreamIndex = 1 });
        items.Add(Start(2));

        // act
        var frames = await RunAndCollect(items, 1);

        // assert
        frames.Should().BeEmpty();
    }

    [Fact]
    public async Task TheStartingTrackIsNeverDropped()
    {
        // arrange
        var items = new List<MuxedAudioStreamItem> { Start(1) };
        items.AddRange(Frames(1, OverBudgetFrameCount));
        items.Add(Start(2));
        items.AddRange(Frames(2, OverBudgetFrameCount));

        // act
        var frames = await RunAndCollect(items, 2);

        // assert
        frames.Count.Should().Be(OverBudgetFrameCount);
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

    private static string StreamIdOf(int index)
        => index.ToString();

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
}
