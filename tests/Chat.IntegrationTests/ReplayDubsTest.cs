using ActualChat.Media;
using ActualChat.Streaming.Services;
using ActualChat.Testing.Host;
using ActualChat.Transcription;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(DubbingTranslationCollection))]
public class ReplayDubsTest(
    DubbingTranslationCollection.AppHostFixture fixture,
    ITestOutputHelper @out)
    : SharedAppHostTestBase<DubbingTranslationCollection.AppHostFixture>(fixture, @out)
{
    private WebClientTester Tester => field ??= AppHost.NewWebClientTester(Out);

    [Fact(Timeout = 90_000)]
    public async Task TheFirstRequestCreatesTheDubAndTheSecondReusesIt()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var services = Tester.AppServices;
        var dubs = services.GetRequiredService<ReplayDubs>();
        var recorder = services.GetRequiredService<RecordingSpeechSynthesizer>();
        var entry = await Tester.RecordVoiceEntry(chatId, Languages.Russian);
        var ct = CancellationToken.None;

        // act
        var first = await dubs.GetOrCreate(entry, Languages.English, ct);
        var translation = await services.WhenReplayDubStored(TranslationId.New(entry.Id, Languages.English), ct);
        var second = await dubs.GetOrCreate(entry, Languages.English, ct);

        // assert
        first.Should().NotBeNull();
        (first!.Stored ?? (object?)first.Live).Should().NotBeNull("the first caller gets the dub in some form");
        second.Should().NotBeNull();
        second!.Live.Should().BeNull("the dub is stored by now");
        second.Stored!.Id.Should().Be(translation.DubMediaId, "the dub is stored on the translation and reused");
        var spoken = recorder.GetChunks(
            RecordingSpeechSynthesizer.OneShotStreamId(Languages.English, translation.Content));
        spoken.Should().Equal([translation.Content], "synthesized once, from the stored translation");
    }

    [Fact(Timeout = 90_000)]
    public async Task TheFirstRequestStreamsTheDubWhileItIsBeingStored()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var services = Tester.AppServices;
        var dubs = services.GetRequiredService<ReplayDubs>();
        var translations = services.GetRequiredService<ITranslationsBackend>();
        var recorder = services.GetRequiredService<RecordingSpeechSynthesizer>();
        var entry = await Tester.RecordVoiceEntry(chatId, Languages.Russian);
        var ct = CancellationToken.None;
        var id = TranslationId.New(entry.Id, Languages.English);
        var gate = TaskCompletionSourceExt.New();
        recorder.OneShotGate = _ => gate.Task;
        try {
            // act
            var first = await dubs.GetOrCreate(entry, Languages.English, ct);

            // assert
            first.Should().NotBeNull();
            first!.Stored.Should().BeNull("nothing can be stored while the synthesis is held");
            first.Live.Should().NotBeNull("the caller gets the dub as it's being made");
            var translation = await translations.Get(id, false, ct);
            translation!.HasValidDub().Should().BeFalse();
            var framesTask = first.Live!.GetFrames(ct).ToListAsync(ct).AsTask();
            await Task.Delay(200, ct);
            framesTask.IsCompleted.Should().BeFalse("the frames are held by the gate");
            dubs.InFlightCount.Should().BeGreaterThanOrEqualTo(1, "the run stays in flight until the dub is stored");

            gate.SetResult();
            var frames = await framesTask.WaitAsync(TimeSpan.FromSeconds(10), ct);
            frames.Should().NotBeEmpty("the live dub carries the synthesized frames once the gate opens");
            translation = await services.WhenReplayDubStored(id, ct);
            var second = await dubs.GetOrCreate(entry, Languages.English, ct);
            second!.Live.Should().BeNull();
            second.Stored!.Id.Should().Be(translation.DubMediaId, "a caller arriving after the store gets the media");
        }
        finally {
            recorder.OneShotGate = null;
            gate.TrySetResult();
        }
    }

    [Fact(Timeout = 90_000)]
    public async Task AnEntryAlreadyInTheListenersLanguageIsNotDubbed()
    {
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var dubs = Tester.AppServices.GetRequiredService<ReplayDubs>();
        var entry = await Tester.RecordVoiceEntry(chatId, Languages.English);

        var dub = await dubs.GetOrCreate(entry, Languages.English, CancellationToken.None);

        dub.Should().BeNull();
    }

    [Fact(Timeout = 90_000)]
    public async Task ARetranslationRegeneratesTheDub()
    {
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var services = Tester.AppServices;
        var dubs = services.GetRequiredService<ReplayDubs>();
        var mediaBackend = services.GetRequiredService<IMediaBackend>();
        var recorder = services.GetRequiredService<RecordingSpeechSynthesizer>();
        var commander = services.Commander();
        var entry = await Tester.RecordVoiceEntry(chatId, Languages.Russian);
        var ct = CancellationToken.None;
        var id = TranslationId.New(entry.Id, Languages.English);
        await dubs.GetOrCreate(entry, Languages.English, ct);
        var translation = await services.WhenReplayDubStored(id, ct);
        var firstMediaId = translation.DubMediaId!;
        var newContent = translation.Content + " Again.";
        var diff = new TranslationDiff { Content = newContent, SourceContentHash = translation.SourceContentHash };
        await commander.Call(new TranslationsBackend_Change(id, translation.Version, Change.Update(diff)));

        var second = await dubs.GetOrCreate(entry, Languages.English, ct);
        translation = await services.WhenReplayDubStored(id, ct);

        second.Should().NotBeNull();
        second!.Stored.Should().BeNull("the old dub spoke the old text, so a new one is being made");
        translation.DubMediaId.Should().NotBe(firstMediaId);
        var spoken = recorder.GetChunks(RecordingSpeechSynthesizer.OneShotStreamId(Languages.English, newContent));
        spoken.Should().Equal([newContent], "the new dub is synthesized from the re-translated content");
        var oldMedia = await mediaBackend.Get(firstMediaId, ct);
        oldMedia.Should().BeNull("the superseded dub's media must not linger once it's replaced");
    }

    [Fact(Timeout = 90_000)]
    public async Task InFlightEntryIsForgottenAfterCompletion()
    {
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var dubs = Tester.AppServices.GetRequiredService<ReplayDubs>();
        var entry = await Tester.RecordVoiceEntry(chatId, Languages.English);
        var ct = CancellationToken.None;

        // Both calls resolve on the synchronously-completing "already spoken in" guard, which is
        // exactly the case that used to leak a permanently-cached null task into the in-flight map
        await dubs.GetOrCreate(entry, Languages.English, ct);
        dubs.InFlightCount.Should().Be(0, "a completed run must remove its own map entry");

        await dubs.GetOrCreate(entry, Languages.English, ct);
        dubs.InFlightCount.Should().Be(0, "a second, synchronously-completing call must clean up too");
    }
}
