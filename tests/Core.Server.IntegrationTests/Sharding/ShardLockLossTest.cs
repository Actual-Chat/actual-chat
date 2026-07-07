using ActualChat.Testing.Host;

namespace ActualChat.Core.Server.IntegrationTests.Sharding;

[Trait("Category", "Slow")]
public class ShardLockLossTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(ShardLockLossTest)}",
        TestAppHostOptions.None with { MustStart = true }, @out)
{
    [Fact]
    public async Task LockLossMustDowngradeShardState()
    {
        // Reproduces the stale-ownership hole: when the shard's lock is lost while the mesh
        // map is unchanged, ShardState keeps claiming OwnedByThisNode with a dead lock,
        // so RequireShardOwnership hands out an Ownership another node may already hold.

        // arrange
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var cancellationToken = cts.Token;
        var shardScheme = ShardScheme.TestBackend;
        const int shard = 0;

        await using var h1 = await NewAppHost();
        var owner = h1.Services.ShardOwner(shardScheme);
        var shardState = owner.States[shard];
        await ComputedTest.When(async ct => {
            var state = await shardState.Use(ct).ConfigureAwait(false);
            state.OwnershipStatus.Should().Be(ShardOwnershipStatus.OwnedByThisNode);
        }, TimeSpan.FromSeconds(30));

        var ownership = shardState.Computed.Value.Ownership!;
        // The same locks ShardOwners builds for this scheme, see ShardOwners.OwnershipLocks
        var locks = h1.Services.MeshLocks()
            .WithKeyPrefix(nameof(ShardOwners))
            .WithKeyPrefix(shardScheme.Name);
        var lockKey = shard.Format();
        locks.GetFullKey(lockKey).Should().Be(ownership.LockHolder.FullKey);

        // act: kill the lock behind ShardOwner's back and grab it ourselves,
        // so h1 can't re-acquire it - like another node would during a rebalance
        (await locks.Backend.ForceRelease(lockKey, mustNotify: true, cancellationToken)).Should().BeTrue();
        var blocker = await locks.Lock(lockKey, cancellationToken);
        try {
            // The holder discovers the loss on its next renewal (~5s with MeshLockOptions.Test)
            await TaskExt.NeverEnding(ownership.LockToken).SuppressExceptions()
                .WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);
            WriteLine("The lock loss is detected by the holder");

            // assert
            await ComputedTest.When(async ct => {
                var state = await shardState.Use(ct).ConfigureAwait(false);
                WriteLine($"ShardState: {state.OwnershipStatus}, lock cancelled: "
                    + $"{state.Ownership?.LockToken.IsCancellationRequested.ToString() ?? "no ownership"}");
                var isStaleOwned = state.Ownership is { } o && o.LockToken.IsCancellationRequested;
                isStaleOwned.Should().BeFalse(
                    "the shard state must not claim ownership backed by a lost lock");
            }, TimeSpan.FromSeconds(10));

            // And RequireShardOwnership must not hand out a dead ownership either
            var ownershipTask = owner
                .RequireShardOwnership(ShardKey.New(shard), addDependency: false, cancellationToken)
                .AsTask();
            await Task.WhenAny(ownershipTask, Task.Delay(TimeSpan.FromSeconds(2), cancellationToken));
            if (ownershipTask is { IsCompletedSuccessfully: true, Result.LockToken.IsCancellationRequested: true })
                Assert.Fail("RequireShardOwnership returned an ownership whose lock is already lost");
        }
        finally {
            await blocker.DisposeAsync();
        }

        // Recovery: once the lock is available again, h1 must re-acquire it
        await ComputedTest.When(async ct => {
            var state = await shardState.Use(ct).ConfigureAwait(false);
            state.OwnershipStatus.Should().Be(ShardOwnershipStatus.OwnedByThisNode);
            state.Ownership!.LockToken.IsCancellationRequested.Should().BeFalse();
        }, TimeSpan.FromSeconds(30));
    }
}
