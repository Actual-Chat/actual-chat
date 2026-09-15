using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class SharedLocationsTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    // Explicit, so a second tester can sign in as the same user: a blank identity mints a fresh one per call.
    private readonly string _aliceIdentity = $"alice-{Ulid.NewUlid()}";

    private WebClientTester Alice => field ??= fixture.AppHost.NewWebClientTester(Out);
    private WebClientTester Bob => field ??= fixture.AppHost.NewWebClientTester(Out);
    private WebClientTester? _aliceOtherDevice;

    protected override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await Alice.SignInAsAlice(_aliceIdentity);
        await Bob.SignInAsBob();
    }

    protected override async Task DisposeAsync()
    {
        await Alice.DisposeSilentlyAsync();
        await Bob.DisposeSilentlyAsync();
        await _aliceOtherDevice.DisposeSilentlyAsync();
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
        location.Point.Should().Be(point);
        location.IsLive(Clocks.SystemClock.Now).Should().BeFalse();

        // assert - a frozen one-shot is not listed among active live shares
        (await sharedLocations.ListLive(session, chatId, ct)).Count.Should().Be(0);

        // assert - the LocationId round-trips through the entry read path
        var reread = await chats.GetEntry(session, entry.Id, ct);
        reread!.LocationId.Should().Be(entry.LocationId);
    }

    [Fact]
    public async Task PlaceIsPersistedAndDoesNotAffectOrdinaryShares()
    {
        // arrange
        var sharedLocations = Alice.AppServices.GetRequiredService<ISharedLocations>();
        var session = Alice.Session;
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Picked place" });
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = cts.Token;
        var placePoint = new GeoPoint(51.5007, -0.1246);
        var ownPoint = new GeoPoint(51.5074, -0.1278, 12f, 90f);

        // act
        var place = await Alice.ReportLocation(chatId, placePoint, isPlace: true, cancellationToken: ct);
        var own = await Alice.ReportLocation(chatId, ownPoint, cancellationToken: ct);

        // assert - IsPlace survives the DB round-trip, and only the picked point carries it
        (await sharedLocations.Get(session, chatId, place.Id, ct))!.IsPlace.Should().BeTrue();
        (await sharedLocations.Get(session, chatId, own.Id, ct))!.IsPlace.Should().BeFalse();

        // assert - a place is still a frozen one-shot pin, so it never joins the live shares
        place.Duration.Should().Be(TimeSpan.Zero);
        place.IsLive(Clocks.SystemClock.Now).Should().BeFalse();
        (await sharedLocations.ListLive(session, chatId, ct)).Count.Should().Be(0);
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

        // act - update position
        var point2 = new GeoPoint(48.8566, 2.3522);
        await Alice.ReportLocation(chatId, point2, TimeSpan.FromHours(1), locationId, cancellationToken: ct);

        // assert - position reflects the update
        await cList.When(x => Math.Abs(x.Single().Point.Latitude - point2.Latitude) < 1e-9, ct)
            .WaitAsync(TimeSpan.FromSeconds(5), ct);

        // act - stop sharing
        await Alice.StopSharingLocation(chatId, locationId, ct);

        // assert - no longer live, but the last position is frozen and kept (not scrubbed)
        await cList.When(x => x.Count == 0, ct).WaitAsync(TimeSpan.FromSeconds(5), ct);
        var frozen = await sharedLocations.Get(session, chatId, locationId, ct);
        frozen.Should().NotBeNull();
        frozen.Point.Should().Be(point2);
        frozen.IsLive(Clocks.SystemClock.Now).Should().BeFalse();
        frozen.Version.Should().BeGreaterThan(shared.Version);
    }

    [Fact]
    public async Task OnlyAuthorCanUpdateAndStopLocation()
    {
        // arrange
        var sharedLocations = Alice.AppServices.GetRequiredService<ISharedLocations>();
        var (chatId, inviteId) = await Alice.CreateChat(x => x with { Title = "Shared location ownership" });
        await Bob.JoinChat(chatId, inviteId);
        var (bobChatId, _) = await Bob.CreateChat(x => x with { Title = "Bob's chat" });
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = cts.Token;
        var originalPoint = new GeoPoint(51.5074, -0.1278);
        var location = await Alice.ReportLocation(chatId, originalPoint, TimeSpan.FromHours(1), cancellationToken: ct);
        var alicePoint = new GeoPoint(48.8566, 2.3522);

        // act
        var bobUpdateError = await Record.ExceptionAsync(
            () => Bob.ReportLocation(chatId, new GeoPoint(40.7128, -74.0060), id: location.Id, cancellationToken: ct));
        var bobStopError = await Record.ExceptionAsync(
            () => Bob.StopSharingLocation(bobChatId, location.Id, ct));
        var updated = await Alice.ReportLocation(chatId, alicePoint, id: location.Id, cancellationToken: ct);
        await Alice.StopSharingLocation(chatId, location.Id, ct);

        // assert
        bobUpdateError.Should().BeOfType<UnauthorizedAccessException>()
            .Which.Message.Should().Contain("only your own shared locations");
        bobStopError.Should().BeOfType<UnauthorizedAccessException>()
            .Which.Message.Should().Contain("only your own shared locations");
        updated.Point.Should().Be(alicePoint);
        var stopped = await sharedLocations.Get(Alice.Session, chatId, location.Id, ct);
        stopped.Should().NotBeNull();
        stopped.Point.Should().Be(alicePoint);
        stopped.IsLive(Clocks.SystemClock.Now).Should().BeFalse();
    }

    [Fact]
    public async Task CannotAttachLocationFromAnotherChat()
    {
        // arrange
        var (sourceChatId, _) = await Alice.CreateChat(x => x with { Title = "Source location chat" });
        var (targetChatId, _) = await Alice.CreateChat(x => x with { Title = "Target location chat" });
        var location = await Alice.ReportLocation(sourceChatId, new GeoPoint(51.5074, -0.1278));
        var command = new Chats_UpsertEntry {
            Session = Alice.Session,
            ChatId = targetChatId,
            LocalId = null,
            LocationId = location.Id,
        };

        // act
        var error = await Record.ExceptionAsync(() => Alice.Commander.Call(command));

        // assert
        error.Should().BeOfType<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task NewLiveShareTakesOverTheRunningOne()
    {
        // arrange
        var sharedLocations = Alice.AppServices.GetRequiredService<ISharedLocations>();
        var session = Alice.Session;
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Takeover" });
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = cts.Token;

        var cList = await Computed.Capture(() => sharedLocations.ListLive(session, chatId, ct), ct);
        var hour = TimeSpan.FromHours(1);
        var firstPoint = new GeoPoint(10, 20);
        var secondPoint = new GeoPoint(30, 40);

        // act - start a first live share, then a second one as another device would
        var first = await Alice.ReportLocation(chatId, firstPoint, hour, cancellationToken: ct);
        await cList.When(x => x.Count == 1, ct).WaitAsync(TimeSpan.FromSeconds(5), ct);
        var second = await Alice.ReportLocation(chatId, secondPoint, hour, cancellationToken: ct);

        // assert - the newest share wins: a new row, and the only live one
        second.Id.Should().NotBe(first.Id, "a takeover mints a new row rather than reusing the old one");
        second.Point.Should().Be(secondPoint);
        await cList.When(x => x.Count == 1 && x[0].Id == second.Id, ct).WaitAsync(TimeSpan.FromSeconds(5), ct);

        // assert - the first is frozen at its last point
        var frozen = await sharedLocations.Get(session, chatId, first.Id, ct);
        frozen.Should().NotBeNull();
        frozen.Point.Should().Be(firstPoint);
        frozen.IsLive(Clocks.SystemClock.Now).Should().BeFalse();
        frozen.Version.Should().BeGreaterThan(first.Version);
    }

    [Fact]
    public async Task TakenOverShareIgnoresFurtherUpdates()
    {
        // arrange - a takeover, leaving the first share frozen
        var sharedLocations = Alice.AppServices.GetRequiredService<ISharedLocations>();
        var session = Alice.Session;
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Stale device" });
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = cts.Token;
        var hour = TimeSpan.FromHours(1);
        var first = await Alice.ReportLocation(chatId, new GeoPoint(10, 20), hour, cancellationToken: ct);
        var secondPoint = new GeoPoint(30, 40);
        var second = await Alice.ReportLocation(chatId, secondPoint, hour, cancellationToken: ct);
        var frozen = await sharedLocations.Get(session, chatId, first.Id, ct);

        // act - the losing device keeps reporting into the id it holds, as an un-updated app does forever
        var result = await Alice.ReportLocation(chatId, new GeoPoint(50, 60), id: first.Id, cancellationToken: ct);

        // assert - answered with the frozen share; nothing moved, nothing bumped, the live one untouched
        result.Should().Be(frozen, "a push into a frozen share is turned away with that share");
        (await sharedLocations.Get(session, chatId, first.Id, ct)).Should().Be(frozen);
        var live = (await sharedLocations.ListLive(session, chatId, ct)).Single();
        live.Id.Should().Be(second.Id);
        live.Point.Should().Be(secondPoint);
    }

    [Fact]
    public async Task FrozenShareUpdateStillChecksOwnership()
    {
        // arrange - Alice's share, stopped
        var (chatId, inviteId) = await Alice.CreateChat(x => x with { Title = "Frozen ownership" });
        await Bob.JoinChat(chatId, inviteId);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = cts.Token;
        var location = await Alice
            .ReportLocation(chatId, new GeoPoint(10, 20), TimeSpan.FromHours(1), cancellationToken: ct);
        await Alice.StopSharingLocation(chatId, location.Id, ct);

        // act
        var updateError = await Record.ExceptionAsync(
            () => Bob.ReportLocation(chatId, new GeoPoint(30, 40), id: location.Id, cancellationToken: ct));
        var stopError = await Record.ExceptionAsync(() => Bob.StopSharingLocation(chatId, location.Id, ct));

        // assert - the frozen-share early-out is no way to touch someone else's share
        updateError.Should().BeOfType<UnauthorizedAccessException>();
        stopError.Should().BeOfType<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task AnyDeviceCanStopTheAuthorsLiveShare()
    {
        // arrange - Alice shares from one device and holds a second one
        var sharedLocations = Alice.AppServices.GetRequiredService<ISharedLocations>();
        var session = Alice.Session;
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Stop from anywhere" });
        var otherDevice = await SignInAliceOtherDevice();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = cts.Token;
        var cList = await Computed.Capture(() => sharedLocations.ListLive(session, chatId, ct), ct);
        var location = await Alice
            .ReportLocation(chatId, new GeoPoint(10, 20), TimeSpan.FromHours(1), cancellationToken: ct);
        await cList.When(x => x.Count == 1, ct).WaitAsync(TimeSpan.FromSeconds(5), ct);

        // act - the other device stops the share it never started
        await otherDevice.StopSharingLocation(chatId, location.Id, ct);

        // assert
        await cList.When(x => x.Count == 0, ct).WaitAsync(TimeSpan.FromSeconds(5), ct);
        var stopped = await sharedLocations.Get(session, chatId, location.Id, ct);
        stopped!.IsLive(Clocks.SystemClock.Now).Should().BeFalse();
    }

    [Fact]
    public async Task StopOfAFrozenOwnShareStopsTheLiveOne()
    {
        // arrange - a takeover, so the id the losing device holds is frozen
        var sharedLocations = Alice.AppServices.GetRequiredService<ISharedLocations>();
        var session = Alice.Session;
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Stop by a frozen id" });
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = cts.Token;
        var cList = await Computed.Capture(() => sharedLocations.ListLive(session, chatId, ct), ct);
        var hour = TimeSpan.FromHours(1);
        var first = await Alice.ReportLocation(chatId, new GeoPoint(10, 20), hour, cancellationToken: ct);
        var second = await Alice.ReportLocation(chatId, new GeoPoint(30, 40), hour, cancellationToken: ct);
        await cList.When(x => x.Count == 1 && x[0].Id == second.Id, ct).WaitAsync(TimeSpan.FromSeconds(5), ct);

        // act - the losing device stops by the id it holds, as an old app does
        await Alice.StopSharingLocation(chatId, first.Id, ct);

        // assert - read as "stop my live share here"
        await cList.When(x => x.Count == 0, ct).WaitAsync(TimeSpan.FromSeconds(5), ct);
    }

    [Fact]
    public async Task StopOfAFrozenShareWithNothingLiveIsANoOp()
    {
        // arrange - a share stopped the ordinary way
        var sharedLocations = Alice.AppServices.GetRequiredService<ISharedLocations>();
        var session = Alice.Session;
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Stop twice" });
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = cts.Token;
        var location = await Alice
            .ReportLocation(chatId, new GeoPoint(10, 20), TimeSpan.FromHours(1), cancellationToken: ct);
        await Alice.StopSharingLocation(chatId, location.Id, ct);
        var frozen = await sharedLocations.Get(session, chatId, location.Id, ct);

        // act
        await Alice.StopSharingLocation(chatId, location.Id, ct);

        // assert - nothing to retarget to, so the frozen share is left exactly as it was
        (await sharedLocations.Get(session, chatId, location.Id, ct)).Should().Be(frozen);
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

        // act - share for a short window via the backend command: the front-end accepts only menu durations
        var author = await Alice.GetOwnAuthor(chatId).Require();
        var change = Change.Create(new SharedLocationDiff {
            Point = new GeoPoint(10, 20),
            LiveDuration = TimeSpan.FromSeconds(2),
        });
        var location = await Alice.Commander.Call(new SharedLocationsBackend_Change(null, author.Id, change), ct);
        var locationId = location!.Id;
        await cList.When(x => x.Count == 1, ct).WaitAsync(TimeSpan.FromSeconds(5), ct);

        // assert - it drops out of the live list on its own once expired (no stop command)
        await cList.When(x => x.Count == 0, ct).WaitAsync(TimeSpan.FromSeconds(10), ct);

        // assert - the frozen record is still readable as the message's history backing
        var frozen = await sharedLocations.Get(session, chatId, locationId, ct);
        frozen.Should().NotBeNull();
    }

    [Fact]
    public async Task NonMenuDurationIsRejected()
    {
        // arrange
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Bad duration" });

        // act & assert - only Constants.Location.Durations options are accepted
        await FluentActions
            .Awaiting(() => Alice.ReportLocation(chatId, new GeoPoint(1, 2), TimeSpan.FromSeconds(30)))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*duration*");
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

    // Private methods

    private async Task<WebClientTester> SignInAliceOtherDevice()
    {
        _aliceOtherDevice = fixture.AppHost.NewWebClientTester(Out);
        await _aliceOtherDevice.SignInAsAlice(_aliceIdentity);
        return _aliceOtherDevice;
    }
}
