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

        // act - post a location-only message with no live duration
        var entry = await Alice.Commander.Call(
            new Chats_UpsertEntry(session, chatId, null) { Location = point }, ct);

        // assert - the entry references a shared location that is frozen (not live)
        entry.LocationId.Should().NotBeNull();
        var location = await sharedLocations.Get(session, chatId, entry.LocationId!, ct);
        location.Should().NotBeNull();
        location!.Point.Should().Be(point);
        location.IsLive(Clocks.SystemClock.Now).Should().BeFalse();

        // assert - a frozen one-shot is not listed among active live shares
        (await sharedLocations.List(session, chatId, ct)).Count.Should().Be(0);
        (await sharedLocations.IsSharing(session, chatId, ct)).Should().BeFalse();

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

        var cList = await Computed.Capture(() => sharedLocations.List(session, chatId, ct), ct);
        cList.Value.Count.Should().Be(0);

        // act - start a live share by posting a location message with a duration
        var point = new GeoPoint(51.5074, -0.1278, 12f, 90f);
        var entry = await Alice.Commander.Call(
            new Chats_UpsertEntry(session, chatId, null) {
                Location = point, LiveDuration = TimeSpan.FromHours(1),
            }, ct);
        var locationId = entry.LocationId!;

        // assert - visible via List/Get/IsSharing
        await cList.When(x => x.Count == 1, ct).WaitAsync(TimeSpan.FromSeconds(5), ct);
        var shared = (await sharedLocations.List(session, chatId, ct)).Single();
        shared.Id.Should().Be(locationId);
        shared.AuthorId.Should().Be(author.Id);
        shared.Point.Should().Be(point);
        (await sharedLocations.IsSharing(session, chatId, ct)).Should().BeTrue();

        // act - update position
        var point2 = new GeoPoint(48.8566, 2.3522);
        await Alice.Commander.Call(new SharedLocations_Report(session, chatId, locationId, point2), ct);

        // assert - position reflects the update
        await cList.When(x => x.Single().Point.Latitude == 48.8566, ct).WaitAsync(TimeSpan.FromSeconds(5), ct);

        // act - stop sharing
        await Alice.Commander.Call(new SharedLocations_Stop(session, chatId, locationId), ct);

        // assert - no longer live, but the last position is frozen and kept (not scrubbed)
        await cList.When(x => x.Count == 0, ct).WaitAsync(TimeSpan.FromSeconds(5), ct);
        (await sharedLocations.IsSharing(session, chatId, ct)).Should().BeFalse();
        var frozen = await sharedLocations.Get(session, chatId, locationId, ct);
        frozen.Should().NotBeNull();
        frozen!.Point.Should().Be(point2);
        frozen.IsLive(Clocks.SystemClock.Now).Should().BeFalse();
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

        var cList = await Computed.Capture(() => sharedLocations.List(session, chatId, ct), ct);

        // act - share for a short window
        var entry = await Alice.Commander.Call(
            new Chats_UpsertEntry(session, chatId, null) {
                Location = new GeoPoint(10, 20), LiveDuration = TimeSpan.FromSeconds(2),
            }, ct);
        await cList.When(x => x.Count == 1, ct).WaitAsync(TimeSpan.FromSeconds(5), ct);

        // assert - it drops out of the live list on its own once expired (no stop command)
        await cList.When(x => x.Count == 0, ct).WaitAsync(TimeSpan.FromSeconds(10), ct);

        // assert - the frozen record is still readable as the message's history backing
        var frozen = await sharedLocations.Get(session, chatId, entry.LocationId!, ct);
        frozen.Should().NotBeNull();
    }

    [Fact]
    public async Task NonMemberCannotPostLocation()
    {
        // arrange
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Private", IsPublic = false });

        // act & assert - Bob is not a member, so posting a location message is rejected
        await Assert.ThrowsAnyAsync<Exception>(() => Bob.Commander.Call(
            new Chats_UpsertEntry(Bob.Session, chatId, null) { Location = new GeoPoint(1, 2) }));
    }
}
