using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(ChatUICollection))]
public class LocationUITest(ChatAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatAppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task ShouldInvalidateSharingRemainingTextWhenSharingStops()
    {
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();

        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.GetOwnAuthor(chatId).Require();
        var locationUI = tester.ScopedAppServices.AppUIHub().LocationUI;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = cts.Token;

        var entry = await tester.CreateLocationEntry(
            chatId,
            new GeoPoint(51.5074, -0.1278, 12f, 90f),
            TimeSpan.FromHours(1),
            ct);
        var locationId = entry.LocationId.Require();

        var computed = await Computed.Capture(
            () => locationUI.GetRemainingTimeText(author.Id, ct),
            ct);
        computed.Value.Should().NotBeEmpty();
        computed.IsConsistent().Should().BeTrue();

        await tester.StopSharingLocation(chatId, locationId, ct);
        computed.IsConsistent().Should().BeFalse();

        computed = await computed.Update(ct);
        computed.Value.Should().Be("");
        computed.IsConsistent().Should().BeTrue();
    }
}
