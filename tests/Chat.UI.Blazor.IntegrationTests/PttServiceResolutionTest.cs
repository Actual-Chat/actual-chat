using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

// Guards proxy/registration mismatches that only surface on first real resolution -
// a sealed class registered as a Fusion compute service has no generated proxy and
// throws only when a live scope resolves it (see PttReplyUI, E1..cleanup).

[Collection(nameof(ChatUICollection))]
public class PttServiceResolutionTest(ChatAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatAppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task ScopedPttServicesResolve()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var services = tester.ScopedAppServices;
        var hub = services.AppUIHub();

        // act + assert
        hub.PttReplyUI.Should().NotBeNull();
        hub.IncomingVoiceActivityUI.Should().NotBeNull();
        hub.GestureUI.Should().NotBeNull();
        services.GetRequiredService<PttSessionCore>().Should().NotBeNull();
    }
}
