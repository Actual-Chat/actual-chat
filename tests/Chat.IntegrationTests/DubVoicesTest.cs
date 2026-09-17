using ActualChat.Testing.Host;
using ActualChat.Transcription;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(DubbingTranslationCollection))]
public class DubVoicesTest(
    DubbingTranslationCollection.AppHostFixture fixture,
    ITestOutputHelper @out)
    : SharedAppHostTestBase<DubbingTranslationCollection.AppHostFixture>(fixture, @out)
{
    private WebClientTester Tester => field ??= AppHost.NewWebClientTester(Out);

    [Fact(Timeout = 60_000)]
    public async Task ListDubVoicesShouldReturnTheSynthesizersCatalog()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var translations = Tester.AppServices.GetRequiredService<ITranslations>();

        // act
        var voices = await translations.ListDubVoices(Tester.Session, CancellationToken.None);

        // assert
        voices.Should().Equal(FakeSpeechSynthesizer.Voices);
    }

    [Fact(Timeout = 60_000)]
    public async Task ListSuggestedDubVoicesShouldFollowTheSpokenLanguages()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var translations = Tester.AppServices.GetRequiredService<ITranslations>();
        var languageSettings = Tester.AppServices.UserSettingsUI(Tester.Session).UserLanguageSettings();
        var ct = CancellationToken.None;

        // act - the default (en-US) speaker gets the american voice
        var suggested = await translations.ListSuggestedDubVoices(Tester.Session, ct);

        // assert
        suggested.Select(x => x.Id).Should().Equal("Adrian");

        // act - a British speaker gets the british voices, then the american one as padding
        await languageSettings.Update(x => x with { Primary = Languages.EnglishUK }, ct);
        var computed = await Computed.Capture(() => translations.ListSuggestedDubVoices(Tester.Session, ct));
        computed = await computed.When(x => x.Count > 1, ct).WaitAsync(TimeSpan.FromSeconds(10), ct);

        // assert
        computed.Value.Select(x => x.Id).Should().Equal("Daniel", "Nina", "Adrian");
    }

    [Fact(Timeout = 60_000)]
    public async Task GetDubVoicePreviewShouldReturnAnMp3ForAKnownVoice()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var translations = Tester.AppServices.GetRequiredService<ITranslations>();

        // act
        var mp3 = await translations.GetDubVoicePreview(
            Tester.Session, "Daniel", Languages.English, CancellationToken.None);

        // assert
        mp3.Should().NotBeNull();
        (mp3![0] == 0xFF && (mp3[1] & 0xE0) == 0xE0).Should().BeTrue("the fake speaks one MP3 frame of silence");
    }

    [Fact(Timeout = 60_000)]
    public async Task GetDubVoicePreviewShouldReturnNullForAnUnknownVoice()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var translations = Tester.AppServices.GetRequiredService<ITranslations>();

        // act
        var mp3 = await translations.GetDubVoicePreview(
            Tester.Session, "Nobody", Languages.English, CancellationToken.None);

        // assert
        mp3.Should().BeNull();
    }

    // There is no "bad language" case any more: Language is now the parameter type (not a raw
    // query string), so an unparseable code is rejected at deserialization, before this method runs.
}
