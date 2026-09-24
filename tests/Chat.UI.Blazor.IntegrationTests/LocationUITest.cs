using ActualChat.Localization;
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
    private BlazorTester? _otherDevice;

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeSilentlyAsync();
        await _otherDevice.DisposeSilentlyAsync();
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
        computed.Value!.GetText(LanguageStringLocalizer.Get(Languages.English)).Should().NotBeEmpty();
        computed.IsConsistent().Should().BeTrue();

        // act
        await Tester.StopSharingLocation(chatId, locationId, cancellationToken);

        // assert
        computed.IsConsistent().Should().BeFalse();
        computed = await computed.Update(cancellationToken);
        computed.Value.Should().BeNull();
        computed.IsConsistent().Should().BeTrue();
    }

    [Fact]
    public async Task OwnShareOfAnotherDeviceShouldBeLiveButNotThisDevices()
    {
        // arrange
        var otherDevice = await SignInOnTwoDevices();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30).Debuggable());
        var cancellationToken = cts.Token;
        var (chatId, _) = await Tester.CreateChat(true, cancellationToken: cancellationToken);

        // act
        var location = await otherDevice.ReportLocation(
            chatId,
            new GeoPoint(10, 20),
            TimeSpan.FromHours(1),
            cancellationToken: cancellationToken);

        // assert - the author is live, so Stop is offered here; this device's tracker has no share to report on
        await TestWait.When(async ct => {
            var ownLive = await LocationUI.GetOwnLive(chatId, ct);
            ownLive.Should().NotBeNull();
            ownLive!.Id.Should().Be(location.Id);
        });
        (await LocationUI.IsOwnLive(chatId, cancellationToken)).Should().BeTrue();
        (await LocationUI.IsOwnDeviceLive(chatId, cancellationToken)).Should().BeFalse();
        (await LocationUI.GetTrackingError(chatId, cancellationToken)).Should().BeNull();
        (await LocationUI.GetOwnMarkerUnlessSharedElsewhere(chatId, cancellationToken))
            .Should().BeNull("the maps show the share itself, not where this device is");
    }

    [Fact]
    public async Task StopSharingShouldStopTheShareOfAnotherDevice()
    {
        // arrange - this device's reporter never started the share, so only GetOwnLive knows its id
        var otherDevice = await SignInOnTwoDevices();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30).Debuggable());
        var cancellationToken = cts.Token;
        var (chatId, _) = await Tester.CreateChat(true, cancellationToken: cancellationToken);
        var location = await otherDevice.ReportLocation(
            chatId,
            new GeoPoint(10, 20),
            TimeSpan.FromHours(1),
            cancellationToken: cancellationToken);
        await TestWait.When(async ct => (await LocationUI.IsOwnLive(chatId, ct)).Should().BeTrue());

        // act
        await LocationUI.StopSharing(chatId, cancellationToken);

        // assert
        await TestWait.When(async ct => (await LocationUI.GetOwnLive(chatId, ct)).Should().BeNull());
        var stopped = await Hub.SharedLocations.Get(Tester.Session, chatId, location.Id, cancellationToken);
        stopped!.IsLive(Tester.AppServices.Clocks().SystemClock.Now).Should().BeFalse();
    }

    [Fact]
    public async Task TakenOverShareShouldShowItsAuthorOnce()
    {
        // arrange - the message holds the first share's id, and the other device has taken over since
        var otherDevice = await SignInOnTwoDevices();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30).Debuggable());
        var cancellationToken = cts.Token;
        var (chatId, _) = await Tester.CreateChat(true, cancellationToken: cancellationToken);
        var hour = TimeSpan.FromHours(1);
        var entry = await Tester.CreateLocationEntry(chatId, new GeoPoint(10, 20), hour, cancellationToken);
        var firstId = entry.LocationId.Require();

        // act
        var second = await otherDevice.ReportLocation(
            chatId,
            new GeoPoint(30, 40),
            hour,
            cancellationToken: cancellationToken);

        // assert - one marker at the live share, not a second one left behind at the frozen point
        await TestWait.When(async ct => {
            var participants = await LocationUI.ListParticipants(chatId, firstId, ct);
            participants.Select(x => x.Location.Id).Should().Equal(
                [second.Id],
                "the frozen share and the live one belong to the same author");
        });
    }

    // Private methods

    private async Task<BlazorTester> SignInOnTwoDevices()
    {
        var identity = $"bob-{Ulid.NewUlid()}";
        await Tester.SignInAsBob(identity);
        _otherDevice = AppHost.NewBlazorTester(Out);
        await _otherDevice.SignInAsBob(identity);
        return _otherDevice;
    }
}
