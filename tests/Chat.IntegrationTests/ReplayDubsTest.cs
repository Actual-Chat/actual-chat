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
        var translations = services.GetRequiredService<ITranslationsBackend>();
        var recorder = services.GetRequiredService<RecordingSpeechSynthesizer>();
        var entry = await Tester.RecordVoiceEntry(chatId, Languages.Russian);
        var ct = CancellationToken.None;

        // act
        var first = await dubs.GetOrCreate(entry, Languages.English, ct);
        var second = await dubs.GetOrCreate(entry, Languages.English, ct);

        // assert
        first.Should().NotBeNull();
        second.Should().NotBeNull();
        second!.Id.Should().Be(first!.Id, "the dub is stored on the translation and reused");
        var translation = await translations.Get(TranslationId.New(entry.Id, Languages.English), false, ct);
        translation!.HasValidDub().Should().BeTrue();
        translation.DubMediaId.Should().Be(first.Id);
        var spoken = recorder.GetChunks(RecordingSpeechSynthesizer.OneShotStreamId(Languages.English, translation.Content));
        spoken.Should().Equal([translation.Content], "synthesized once, from the stored translation");
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
        var commander = services.Commander();
        var entry = await Tester.RecordVoiceEntry(chatId, Languages.Russian);
        var ct = CancellationToken.None;
        var first = await dubs.GetOrCreate(entry, Languages.English, ct);
        var id = TranslationId.New(entry.Id, Languages.English);
        var translation = await services.GetRequiredService<ITranslationsBackend>().Get(id, false, ct);
        await commander.Call(new TranslationsBackend_Change(id, translation!.Version, Change.Update(new TranslationDiff {
            Content = translation.Content + " Again.",
            SourceContentHash = translation.SourceContentHash,
        })));

        var second = await dubs.GetOrCreate(entry, Languages.English, ct);

        second.Should().NotBeNull();
        second!.Id.Should().NotBe(first!.Id, "the old dub spoke the old text");
    }
}
