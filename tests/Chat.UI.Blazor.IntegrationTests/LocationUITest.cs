using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(ChatUICollection))]
public class LocationUITest(ChatAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatAppHostFixture>(fixture, @out)
{
    private BlazorTester Tester => field ??= AppHost.NewBlazorTester(Out);
    private AppUIHub Hub => field ??= Tester.ScopedAppServices.AppUIHub();
    private LocationUI LocationUI => Hub.LocationUI;

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task ShouldInvalidateSharingRemainingTextWhenSharingStops()
    {
        // arrange
        await Tester.SignInAsUniqueBob();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30).Debuggable());
        var cancellationToken = cts.Token;

        var (chatId, _) = await Tester.CreateChat(true, cancellationToken: cancellationToken);
        var entry = await Tester.CreateLocationEntry(
            chatId,
            new GeoPoint(51.5074, -0.1278, 12f, 90f),
            TimeSpan.FromHours(1),
            cancellationToken);
        var locationId = entry.LocationId.Require();

        var computed = await Computed.Capture(
            () => LocationUI.GetCountdown(chatId, locationId, cancellationToken),
            cancellationToken);
        computed.Value.Should().NotBeNull();
        computed.Value!.Text.Should().NotBeEmpty();
        computed.IsConsistent().Should().BeTrue();

        // act
        await Tester.StopSharingLocation(chatId, locationId, cancellationToken);

        // assert
        computed.IsConsistent().Should().BeFalse();
        computed = await computed.Update(cancellationToken);
        computed.Value.Should().BeNull();
        computed.IsConsistent().Should().BeTrue();
    }
}
