using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class LiveLocationsTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
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
    public async Task ShareLifecycle()
    {
        // arrange
        var liveLocations = Alice.AppServices.GetRequiredService<ILiveLocations>();
        var session = Alice.Session;
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Live locations" });
        var author = await Alice.GetOwnAuthor(chatId).Require();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = cts.Token;

        var cList = await Computed.Capture(() => liveLocations.List(session, chatId, ct), ct);
        cList.Value.Count.Should().Be(0);

        // act - start sharing
        var point = new GeoPoint(51.5074, -0.1278, 12f, 90f);
        await Alice.Commander.Call(new LiveLocations_Start(session, chatId, point, TimeSpan.FromHours(1)), ct);

        // assert - visible via List/Get/IsSharing
        await cList.When(x => x.Count == 1, ct).WaitAsync(TimeSpan.FromSeconds(5), ct);
        var shared = (await liveLocations.List(session, chatId, ct)).Single();
        shared.AuthorId.Should().Be(author.Id);
        shared.Point.Latitude.Should().Be(51.5074);
        shared.Point.Longitude.Should().Be(-0.1278);
        (await liveLocations.Get(session, chatId, author.Id, ct)).Should().NotBeNull();
        (await liveLocations.IsSharing(session, chatId, ct)).Should().BeTrue();

        // act - update position
        var point2 = new GeoPoint(48.8566, 2.3522);
        await Alice.Commander.Call(new LiveLocations_Update(session, chatId, point2), ct);

        // assert - position reflects the update
        await cList.When(x => x.Count == 1 && x.Single().Point.Latitude == 48.8566, ct)
            .WaitAsync(TimeSpan.FromSeconds(5), ct);

        // act - stop sharing
        await Alice.Commander.Call(new LiveLocations_Stop(session, chatId), ct);

        // assert - gone
        await cList.When(x => x.Count == 0, ct).WaitAsync(TimeSpan.FromSeconds(5), ct);
        (await liveLocations.IsSharing(session, chatId, ct)).Should().BeFalse();
    }

    [Fact]
    public async Task AutoExpires()
    {
        // arrange
        var liveLocations = Alice.AppServices.GetRequiredService<ILiveLocations>();
        var session = Alice.Session;
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Expiring share" });
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = cts.Token;

        var cList = await Computed.Capture(() => liveLocations.List(session, chatId, ct), ct);

        // act - share for a short window
        await Alice.Commander.Call(
            new LiveLocations_Start(session, chatId, new GeoPoint(10, 20), TimeSpan.FromSeconds(2)), ct);
        await cList.When(x => x.Count == 1, ct).WaitAsync(TimeSpan.FromSeconds(5), ct);

        // assert - the share disappears on its own once expired (no stop command)
        await cList.When(x => x.Count == 0, ct).WaitAsync(TimeSpan.FromSeconds(10), ct);
    }

    [Fact]
    public async Task NonMemberCannotShare()
    {
        // arrange
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Private", IsPublic = false });
        var bobSession = Bob.Session;

        // act & assert - Bob is not a member, so sharing is rejected
        await Assert.ThrowsAnyAsync<Exception>(() => Bob.Commander.Call(
            new LiveLocations_Start(bobSession, chatId, new GeoPoint(1, 2), TimeSpan.FromHours(1))));
    }
}
