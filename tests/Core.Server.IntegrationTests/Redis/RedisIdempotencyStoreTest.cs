using ActualChat.Commands;
using ActualChat.Redis;
using ActualChat.Testing.Host;

namespace ActualChat.Core.Server.IntegrationTests.Redis;

public class RedisIdempotencyStoreTest(ITestOutputHelper @out)
    : LocalAppHostTestBase($"x-{nameof(RedisIdempotencyStoreTest)}", TestAppHostOptions.None, @out)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(1);

    private RedisIdempotencyStore Store => new(AppHost.Services);

    [Fact(Timeout = 30_000)]
    public async Task ClaimOrGetFirstClaimsThenReportsInProgressOwner()
    {
        // arrange
        var store = Store;
        var key = NewKey();

        // act
        var first = await store.ClaimOrGet(key, "nodeA", Ttl, CancellationToken.None);
        var second = await store.ClaimOrGet(key, "nodeB", Ttl, CancellationToken.None);

        // assert
        first.State.Should().Be(IdempotencyState.New);
        first.Owner.Should().Be("nodeA");
        second.State.Should().Be(IdempotencyState.InProgress);
        second.Owner.Should().Be("nodeA", "the in-progress marker keeps the original owner");
    }

    [Fact(Timeout = 30_000)]
    public async Task CompleteMakesClaimOrGetReplayResult()
    {
        // arrange
        var store = Store;
        var key = NewKey();
        var result = new byte[] { 1, 2, 3 };

        // act
        await store.ClaimOrGet(key, "nodeA", Ttl, CancellationToken.None);
        await store.Complete(key, result, Ttl, CancellationToken.None);
        var entry = await store.ClaimOrGet(key, "nodeB", Ttl, CancellationToken.None);

        // assert
        entry.State.Should().Be(IdempotencyState.Completed);
        entry.Result.ToArray().Should().Equal(result);
    }

    [Fact(Timeout = 30_000)]
    public async Task TryReclaimFromMatchingOwnerTransfersOwnership()
    {
        // arrange
        var store = Store;
        var key = NewKey();

        // act
        await store.ClaimOrGet(key, "deadNode", Ttl, CancellationToken.None);
        var reclaimed = await store.TryReclaim(key, "deadNode", "liveNode", Ttl, CancellationToken.None);
        var afterwards = await store.ClaimOrGet(key, "otherNode", Ttl, CancellationToken.None);

        // assert
        reclaimed.Should().NotBeNull();
        reclaimed!.State.Should().Be(IdempotencyState.New);
        reclaimed.Owner.Should().Be("liveNode");
        afterwards.State.Should().Be(IdempotencyState.InProgress);
        afterwards.Owner.Should().Be("liveNode", "reclaim must rewrite the marker's owner");
    }

    [Fact(Timeout = 30_000)]
    public async Task TryReclaimWrongOwnerReturnsNull()
    {
        // arrange
        var store = Store;
        var key = NewKey();

        // act
        await store.ClaimOrGet(key, "nodeA", Ttl, CancellationToken.None);
        var reclaimed = await store.TryReclaim(key, "someoneElse", "nodeB", Ttl, CancellationToken.None);

        // assert
        reclaimed.Should().BeNull("owner mismatch must not steal a live claim");
    }

    [Fact(Timeout = 30_000)]
    public async Task TryReclaimAfterCompleteReturnsCompletedResult()
    {
        // arrange
        var store = Store;
        var key = NewKey();
        var result = new byte[] { 9, 8, 7 };

        // act
        await store.ClaimOrGet(key, "nodeA", Ttl, CancellationToken.None);
        await store.Complete(key, result, Ttl, CancellationToken.None);
        var reclaimed = await store.TryReclaim(key, "nodeA", "nodeB", Ttl, CancellationToken.None);

        // assert
        reclaimed.Should().NotBeNull();
        reclaimed!.State.Should().Be(IdempotencyState.Completed);
        reclaimed.Result.ToArray().Should().Equal(result);
    }

    [Fact(Timeout = 30_000)]
    public async Task TryReclaimMissingKeyReturnsNull()
    {
        // act
        var reclaimed = await Store.TryReclaim(NewKey(), "nodeA", "nodeB", Ttl, CancellationToken.None);

        // assert
        reclaimed.Should().BeNull();
    }

    [Fact(Timeout = 30_000)]
    public async Task ReleaseAllowsAFreshClaim()
    {
        // arrange
        var store = Store;
        var key = NewKey();

        // act
        await store.ClaimOrGet(key, "nodeA", Ttl, CancellationToken.None);
        await store.Release(key, CancellationToken.None);
        var entry = await store.ClaimOrGet(key, "nodeB", Ttl, CancellationToken.None);

        // assert
        entry.State.Should().Be(IdempotencyState.New);
        entry.Owner.Should().Be("nodeB");
    }

    [Fact(Timeout = 30_000)]
    public async Task WaitForResultReturnsResultAfterComplete()
    {
        // arrange
        var store = Store;
        var key = NewKey();
        var result = new byte[] { 5 };

        // act
        await store.ClaimOrGet(key, "nodeA", Ttl, CancellationToken.None);
        await store.Complete(key, result, Ttl, CancellationToken.None);
        var waited = await store.WaitForResult(key, Ttl, CancellationToken.None);

        // assert
        waited.Should().NotBeNull();
        waited!.Value.ToArray().Should().Equal(result);
    }

    private static string NewKey()
        => $"idem-test:{Guid.NewGuid():N}";
}
