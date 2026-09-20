using ActualChat.Live;
using ActualChat.Streaming;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(DubbingTranslationCollection))]
public class ReplayStreamTest(
    DubbingTranslationCollection.AppHostFixture fixture,
    ITestOutputHelper @out)
    : SharedAppHostTestBase<DubbingTranslationCollection.AppHostFixture>(fixture, @out)
{
    private WebClientTester Tester => field ??= AppHost.NewWebClientTester(Out);

    [Fact(Timeout = 120_000)]
    public async Task AReplayShouldSendFramesInPlaybackOrderWithoutTrailingSilence()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var entries = new List<ChatEntry>();
        for (var i = 0; i < 3; i++) {
            // An entry is dated by its audio, not by the wall clock: 100 frames are 2 s of sound
            // sent in about half of that, so the pause has to outlast the sound for a gap to exist
            if (i > 0)
                await Task.Delay(TimeSpan.FromSeconds(4));
            entries.Add(await Tester.RecordVoiceEntry(chatId, Languages.English, frameCount: 100));
        }
        var liveStreams = Tester.AppServices.GetRequiredService<ILiveAudioStreams>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = cts.Token;

        // act
        var stream = await liveStreams.GetReplayStream(
            Tester.Session, chatId, entries[0].BeginsAt, TimeSpan.Zero, 1.0, null, ct);
        var items = await stream.ToListAsync(ct);

        // assert
        var starts = items.OfType<MuxedAudioStreamStart>().ToList();
        starts.Select(x => x.StreamInfo.EntryId).Should().Equal(entries.Select(x => (ChatEntryId?)x.Id));
        var startByIndex = starts.ToDictionary(x => x.StreamIndex);
        var frames = items.OfType<MuxedAudioFrame>().ToList();
        frames.Select(x => startByIndex[x.StreamIndex].PlaysAt + x.Offset).Should()
            .BeInAscendingOrder("frames must go out in the order they play, not the order their blobs opened");
        for (var i = 0; i < entries.Count; i++) {
            var entry = entries[i];
            var start = starts[i];
            // Frame offsets count from the blob, which starts where the recording did - earlier than
            // the entry's first word, so the cut is checked against that origin rather than the entry's
            var speechEnd = entry.EndsAt!.Value - entry.Audio!.BeginsAt;
            var lastOffset = frames.Where(x => x.StreamIndex == start.StreamIndex).Max(x => x.Offset);
            lastOffset.Should()
                .BeLessThan(speechEnd + Constants.Audio.ReplayTailMargin, "the silence after the last word is cut");
            lastOffset.Should()
                .BeGreaterThan(speechEnd - TimeSpan.FromMilliseconds(200), "the last word itself is kept");
            if (i == 0)
                continue;

            var previousEnd = starts[i - 1].PlaysAt + (entries[i - 1].EndsAt!.Value - entries[i - 1].BeginsAt);
            (entry.BeginsAt - entries[i - 1].EndsAt!.Value).Should().BeGreaterThan(Constants.Audio.ReplayMaxGap);
            (start.PlaysAt - previousEnd).Should().BeCloseTo(
                Constants.Audio.ReplayMaxGap, TimeSpan.FromMilliseconds(20),
                "a pause between entries is cut down to the max gap");
        }
    }
}
