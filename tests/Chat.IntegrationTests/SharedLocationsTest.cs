using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class SharedLocationsTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private WebClientTester Alice => field ??= fixture.AppHost.NewWebClientTester(Out);
    private WebClientTester Bob => field ??= fixture.AppHost.NewWebClientTester(Out);

    protected override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await Alice.SignInAsAlice();
        await Bob.SignInAsBob();
    }

    protected override async Task DisposeAsync()
    {
        await Alice.DisposeSilentlyAsync();
        await Bob.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task OneShotLocationMessage()
    {
        // arrange
        var sharedLocations = Alice.AppServices.GetRequiredService<ISharedLocations>();
        var chats = Alice.AppServices.GetRequiredService<IChats>();
        var session = Alice.Session;
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "One-shot location" });
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = cts.Token;
        var point = new GeoPoint(51.5074, -0.1278, 12f, 90f);

        // act - create a frozen one-shot location, then post a message referencing it
        var entry = await Alice.CreateLocationEntry(chatId, point, cancellationToken: ct);

        // assert - the entry references a shared location that is frozen (not live)
        entry.LocationId.Should().NotBeNull();
        var location = await sharedLocations.Get(session, chatId, entry.LocationId!, ct);
        location.Should().NotBeNull();
        location!.Point.Should().Be(point);
        location.IsLive(Clocks.SystemClock.Now).Should().BeFalse();

        // assert - a frozen one-shot is not listed among active live shares
        (await sharedLocations.ListLive(session, chatId, ct)).Count.Should().Be(0);
        (await sharedLocations.IsOwnSharing(session, chatId, ct)).Should().BeFalse();

        // assert - the LocationId round-trips through the entry read path
        var reread = await chats.GetEntry(session, entry.Id, ct);
        reread!.LocationId.Should().Be(entry.LocationId);
    }

    [Fact]
    public async Task LiveLocationShareLifecycle()
    {
        // arrange
        var sharedLocations = Alice.AppServices.GetRequiredService<ISharedLocations>();
        var session = Alice.Session;
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Live location" });
        var author = await Alice.GetOwnAuthor(chatId).Require();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = cts.Token;

        var cList = await Computed.Capture(() => sharedLocations.ListLive(session, chatId, ct), ct);
        cList.Value.Count.Should().Be(0);

        // act - start a live share: create the live location, then post a message referencing it
        var point = new GeoPoint(51.5074, -0.1278, 12f, 90f);
        var entry = await Alice.CreateLocationEntry(chatId, point, TimeSpan.FromHours(1), ct);
        var locationId = entry.LocationId!;

        // assert - visible via ListLive/Get/IsSharing
        await cList.When(x => x.Count == 1, ct).WaitAsync(TimeSpan.FromSeconds(5), ct);
        var shared = (await sharedLocations.ListLive(session, chatId, ct)).Single();
        shared.Id.Should().Be(locationId);
        shared.AuthorId.Should().Be(author.Id);
        shared.Point.Should().Be(point);
        shared.Version.Should().BeGreaterThan(0);
        (await sharedLocations.IsOwnSharing(session, chatId, ct)).Should().BeTrue();
        (await sharedLocations.IsAnyoneSharing(session, chatId, ct)).Should().BeTrue();

        // act - update position
        var point2 = new GeoPoint(48.8566, 2.3522);
        await Alice.ReportLocation(chatId, point2, TimeSpan.FromHours(1), locationId, ct);

        // assert - position reflects the update
        await cList.When(x => Math.Abs(x.Single().Point.Latitude - point2.Latitude) < 1e-9, ct)
            .WaitAsync(TimeSpan.FromSeconds(5), ct);

        // act - stop sharing
        await Alice.StopSharingLocation(chatId, locationId, ct);

        // assert - no longer live, but the last position is frozen and kept (not scrubbed)
        await cList.When(x => x.Count == 0, ct).WaitAsync(TimeSpan.FromSeconds(5), ct);
        (await sharedLocations.IsOwnSharing(session, chatId, ct)).Should().BeFalse();
        (await sharedLocations.IsAnyoneSharing(session, chatId, ct)).Should().BeFalse();
        var frozen = await sharedLocations.Get(session, chatId, locationId, ct);
        frozen.Should().NotBeNull();
        frozen!.Point.Should().Be(point2);
        frozen.IsLive(Clocks.SystemClock.Now).Should().BeFalse();
        frozen.Version.Should().BeGreaterThan(shared.Version);
    }

    [Fact]
    public async Task NewLiveShareReturnsExistingWhenAlreadySharing()
    {
        // arrange
        var sharedLocations = Alice.AppServices.GetRequiredService<ISharedLocations>();
        var session = Alice.Session;
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Idempotent" });
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = cts.Token;

        var cList = await Computed.Capture(() => sharedLocations.ListLive(session, chatId, ct), ct);
        var hour = TimeSpan.FromHours(1);
        var firstPoint = new GeoPoint(10, 20);

        // act - start a first live share
        var first = await Alice.ReportLocation(chatId, firstPoint, hour, cancellationToken: ct);
        await cList.When(x => x.Count == 1, ct).WaitAsync(TimeSpan.FromSeconds(5), ct);

        // act - the same author starts a second live share while the first is still live
        var second = await Alice.ReportLocation(chatId, new GeoPoint(30, 40), hour, cancellationToken: ct);

        // assert - one live share per author: the running one is returned, no second is created
        second.Id.Should().Be(first.Id);
        second.Point.Should().Be(firstPoint);
        var live = await sharedLocations.ListLive(session, chatId, ct);
        live.Count.Should().Be(1);
        live.Single().Id.Should().Be(first.Id);
    }

    [Fact]
    public async Task LiveShareAutoExpires()
    {
        // arrange
        var sharedLocations = Alice.AppServices.GetRequiredService<ISharedLocations>();
        var session = Alice.Session;
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Expiring live share" });
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = cts.Token;

        var cList = await Computed.Capture(() => sharedLocations.ListLive(session, chatId, ct), ct);

        // act - share for a short window
        var entry = await Alice.CreateLocationEntry(chatId, new GeoPoint(10, 20), TimeSpan.FromSeconds(2), ct);
        var locationId = entry.LocationId!;
        await cList.When(x => x.Count == 1, ct).WaitAsync(TimeSpan.FromSeconds(5), ct);

        // assert - it drops out of the live list on its own once expired (no stop command)
        await cList.When(x => x.Count == 0, ct).WaitAsync(TimeSpan.FromSeconds(10), ct);

        // assert - the frozen record is still readable as the message's history backing
        var frozen = await sharedLocations.Get(session, chatId, locationId, ct);
        frozen.Should().NotBeNull();
    }

    [Fact]
    public async Task NonMemberCannotPostLocation()
    {
        // arrange
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Private", IsPublic = false });

        // act & assert - Bob is not a member, so creating a shared location is rejected
        await FluentActions
            .Awaiting(() => Bob.ReportLocation(chatId, new GeoPoint(1, 2)))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*join the chat*");
    }

    [Fact]
    public async Task NonMemberCannotPostLocationToPublicChat()
    {
        // arrange - a public chat Bob can read and join, but hasn't joined
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Public", IsPublic = true });

        // act & assert - sharing a location must not auto-join: Bob is rejected until he joins
        await FluentActions
            .Awaiting(() => Bob.ReportLocation(chatId, new GeoPoint(1, 2)))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*join the chat*");
    }
}
