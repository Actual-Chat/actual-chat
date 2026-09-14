using ActualChat.Live;
using ActualChat.Streaming;
using ActualChat.Testing.Host;
using ActualChat.Transcription;
using ActualChat.Users;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(DubbingTranslationCollection))]
public class ReplayDubbingTest(
    DubbingTranslationCollection.AppHostFixture fixture,
    ITestOutputHelper @out)
    : SharedAppHostTestBase<DubbingTranslationCollection.AppHostFixture>(fixture, @out)
{
    private WebClientTester Tester => field ??= AppHost.NewWebClientTester(Out);

    [Fact(Timeout = 90_000)]
    public async Task AReplayForAnEnglishListenerSpeaksTheRussianEntryInEnglish()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var services = Tester.AppServices;
        var liveStreams = services.GetRequiredService<ILiveAudioStreams>();
        var recorder = services.GetRequiredService<RecordingSpeechSynthesizer>();
        var entry = await Tester.RecordVoiceEntry(chatId, Languages.Russian);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = cts.Token;
        await services.UserSettingsUI(Tester.Session).UserLanguageSettings()
            .Set(new UserLanguageSettings { Primary = Languages.English }, ct);

        // act
        var stream = await liveStreams.GetReplayStream(
            Tester.Session, chatId, entry.BeginsAt, TimeSpan.Zero, 1.0, Languages.English, ct);
        var items = await stream.ToListAsync(ct);

        // assert
        var start = items.OfType<MuxedAudioStreamStart>().Should().ContainSingle().Subject;
        start.StreamInfo.DubLanguage.Should().Be(Languages.English);
        start.StreamInfo.EntryId.Should().Be(entry.Id);
        items.OfType<MuxedAudioFrame>().Should().NotBeEmpty();
        var translation = await services.GetRequiredService<ITranslationsBackend>()
            .Get(TranslationId.New(entry.Id, Languages.English), false, ct);
        recorder.GetChunks(RecordingSpeechSynthesizer.OneShotStreamId(Languages.English, translation!.Content))
            .Should().Equal([translation.Content]);
    }

    [Fact(Timeout = 90_000)]
    public async Task AReplayWithoutADubLanguageIsUnchanged()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var liveStreams = Tester.AppServices.GetRequiredService<ILiveAudioStreams>();
        var entry = await Tester.RecordVoiceEntry(chatId, Languages.Russian);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        // act
        var stream = await liveStreams.GetReplayStream(
            Tester.Session, chatId, entry.BeginsAt, TimeSpan.Zero, 1.0, null, cts.Token);
        var items = await stream.ToListAsync(cts.Token);

        // assert
        items.OfType<MuxedAudioStreamStart>().Should().ContainSingle().Which.StreamInfo.DubLanguage.Should().BeNull();
    }
}
