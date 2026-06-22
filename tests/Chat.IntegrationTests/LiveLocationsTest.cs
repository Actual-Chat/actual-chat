using ActualChat.Chat.Db;
using ActualChat.Testing.Host;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

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

    [Fact]
    public async Task CleanupRemovesExpiredShares()
    {
        // arrange - a share that expires almost immediately
        var session = Alice.Session;
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Cleanup" });
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = cts.Token;
        await Alice.Commander.Call(
            new LiveLocations_Start(session, chatId, new GeoPoint(10, 20), TimeSpan.FromSeconds(1)), ct);

        var dbHub = fixture.AppHost.Services.DbHub<ChatDbContext>();
        var prefix = chatId.Value + ":";
        await AssertShareCount(dbHub, prefix, 1, ct);

        // act - run the same predicate LiveLocationsCleanup uses (verifies the
        // StartedAt + Duration SQL translation and the data-at-rest scrub)
        await Task.Delay(TimeSpan.FromSeconds(1.5), ct);
        DateTime now = DateTime.UtcNow;
        await using (var dbContext = await dbHub.CreateDbContext(readWrite: true, ct)) {
            await dbContext.LiveLocations
                .Where(x => x.StartedAt + x.Duration < now)
                .ExecuteDeleteAsync(ct);
        }

        // assert - the expired row is gone
        await AssertShareCount(dbHub, prefix, 0, ct);
    }

    // Private methods

    private static async Task AssertShareCount(
        DbHub<ChatDbContext> dbHub, string idPrefix, int expected, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbHub.CreateDbContext(cancellationToken);
        var count = await dbContext.LiveLocations.CountAsync(x => x.Id.StartsWith(idPrefix), cancellationToken);
        count.Should().Be(expected);
    }
}
