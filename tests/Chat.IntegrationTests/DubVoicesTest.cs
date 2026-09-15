using System.Net;
using ActualChat.Security;
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
    public async Task PreviewShouldServeAnMp3ForAKnownVoiceOnly()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var secureTokens = AppHost.Services.GetRequiredService<ISecureTokens>();
        var sessionToken = await secureTokens.CreateForSession(Tester.Session);
        using var client = AppHost.NewHttpClient();
        client.DefaultRequestHeaders.Add(Constants.Session.HeaderName, sessionToken.Token);
        using var anonymousClient = AppHost.NewHttpClient();

        // act
        using var known = await client.GetAsync("api/dub-voices/Daniel/preview?language=en-US");
        using var unknown = await client.GetAsync("api/dub-voices/Nobody/preview?language=en-US");
        using var badLanguage = await client.GetAsync("api/dub-voices/Daniel/preview?language=xx");
        using var anonymous = await anonymousClient.GetAsync("api/dub-voices/Daniel/preview?language=en-US");

        // assert
        known.StatusCode.Should().Be(HttpStatusCode.OK);
        known.Content.Headers.ContentType!.MediaType.Should().Be("audio/mpeg");
        var mp3 = await known.Content.ReadAsByteArrayAsync();
        (mp3[0] == 0xFF && (mp3[1] & 0xE0) == 0xE0).Should().BeTrue("the fake speaks one MP3 frame of silence");
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        badLanguage.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        anonymous.StatusCode.Should().Be(HttpStatusCode.BadRequest, "a preview needs a signed-in user");
    }
}
