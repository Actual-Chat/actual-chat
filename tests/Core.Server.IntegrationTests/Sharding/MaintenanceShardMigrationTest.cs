using ActualChat.Testing.Host;

namespace ActualChat.Core.Server.IntegrationTests.Sharding;

// MaintenancesBackend.Get reads its partition in isolation, so the partition cache carries no
// dependency on the partition's contents. Shard relocation is therefore the one thing that can
// leave a node serving a cache that missed writes made while another node owned the shard.
[Trait("Category", "Slow")]
public class MaintenanceShardMigrationTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(MaintenanceShardMigrationTest)}",
        TestAppHostOptions.Default with { MustStart = true }, @out)
{
    [Fact]
    public async Task ReadsMustFollowShardRelocation()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var cancellationToken = cts.Token;
        var shardScheme = ShardScheme.MaintenanceBackend;
        var shardCount = shardScheme.ShardCount;

        await using var h1 = await NewAppHost();
        var o1 = h1.Services.ShardOwner(shardScheme);
        await TestWait.When(async ct => {
            var bits = await o1.BitmapState.Use(ct).ConfigureAwait(false);
            bits.SetBitCount().Should().Be(shardCount);
        }, TimeSpan.FromSeconds(15));

        // A maintenance key routes by its high hex digit, so the shard index can be chosen directly
        var keys = new MaintenanceKey[shardCount];
        for (var shard = 0; shard < shardCount; shard++) {
            var fullPartitionKey = new ShardKey(((uint)shard << 28) | 0x0abc0000)
                .Head(MaintenanceKey.FullPartitionKeySize);
            keys[shard] = new MaintenanceKey($"test:shard-migration:{shard.Format()}", fullPartitionKey);
            shardScheme.GetShardIndex(keys[shard]).Should().Be(shard);
        }

        // Warms every partition's cache on h1 while it still owns all the shards
        var backend1 = h1.Services.GetRequiredService<IMaintenancesBackend>();
        var commander1 = h1.Services.Commander();
        for (var shard = 0; shard < shardCount; shard++)
            (await backend1.Get(keys[shard], cancellationToken)).Should().Be(MaintenanceMode.None);

        // Half of the shards move to h2
        var h2 = await NewAppHost(o => o with { MustInitializeDb = false });
        var o2 = h2.Services.ShardOwner(shardScheme);
        await TestWait.When(async ct => {
            var bits1 = await o1.BitmapState.Use(ct).ConfigureAwait(false);
            var bits2 = await o2.BitmapState.Use(ct).ConfigureAwait(false);
            bits1.SetBitCount().Should().Be(shardCount / 2);
            bits2.SetBitCount().Should().Be(shardCount / 2);
        }, TimeSpan.FromSeconds(15));

        var movedShards = new List<int>();
        for (var shard = 0; shard < shardCount; shard++)
            if (o2.GetShardStateComputed(shard, addDependency: false).Value.OwnershipStatus
                is ShardOwnershipStatus.OwnedByThisNode or ShardOwnershipStatus.MappedToThisNode)
                movedShards.Add(shard);
        WriteLine($"Shards moved to h2: {movedShards.ToDelimitedString(",")}");
        movedShards.Should().NotBeEmpty();

        try {
            // The commands route to h2, which now owns these shards
            var backend2 = h2.Services.GetRequiredService<IMaintenancesBackend>();
            foreach (var shard in movedShards) {
                await commander1
                    .Call(new MaintenancesBackend_Set(keys[shard], MaintenanceMode.System), true, cancellationToken)
                    .ConfigureAwait(false);
                await TestWait.When(async ct => {
                    (await backend2.Get(keys[shard], ct)).Should().Be(MaintenanceMode.System);
                    (await backend1.Get(keys[shard], ct)).Should().Be(MaintenanceMode.System);
                }, TimeSpan.FromSeconds(20));
            }

            // h2 dies, so its shards return to h1 - whose partition caches predate every write above
            await h2.DisposeAsync();
            await TestWait.When(async ct => {
                var bits1 = await o1.BitmapState.Use(ct).ConfigureAwait(false);
                bits1.SetBitCount().Should().Be(shardCount);
            }, TimeSpan.FromSeconds(30));

            foreach (var shard in movedShards)
                await TestWait.When(async ct => {
                    (await backend1.Get(keys[shard], ct)).Should().Be(MaintenanceMode.System);
                }, TimeSpan.FromSeconds(20));

            // Shards that never moved must still read as untouched
            for (var shard = 0; shard < shardCount; shard++)
                if (!movedShards.Contains(shard))
                    (await backend1.Get(keys[shard], cancellationToken)).Should().Be(MaintenanceMode.None);
        }
        finally {
            foreach (var shard in movedShards)
                await commander1
                    .Call(new MaintenancesBackend_Set(keys[shard], MaintenanceMode.None), true, CancellationToken.None)
                    .SilentAwait(false);
        }
    }
}
