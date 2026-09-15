using ActualChat.Testing.Host;
using ActualChat.Transcription;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(DubbingTranslationCollection))]
public class SonioxVoicesResolutionTest(
    DubbingTranslationCollection.AppHostFixture fixture,
    ITestOutputHelper @out)
    : SharedAppHostTestBase<DubbingTranslationCollection.AppHostFixture>(fixture, @out)
{
    [Fact]
    public void ISonioxVoicesShouldResolveToFakeSonioxVoicesUnderUseFakeTranscriber()
    {
        var services = AppHost.Services;
        var sonioxVoices = services.GetRequiredService<ISonioxVoices>();

        sonioxVoices.Should().BeSameAs(services.GetRequiredService<FakeSonioxVoices>());
    }
}
