using ActualChat.Attributes;
using ActualChat.Testing.Host;
using ActualChat.Rpc;
using ActualLab.Rpc;

namespace ActualChat.Core.Server.IntegrationTests.Sharding;

[Trait("Category", "Slow")]
public class ShardMigrationComputedTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(ShardMigrationComputedTest)}",
        TestAppHostOptions.None with {
            MustStart = true,
            ConfigureServices = (ctx, services) => {
                var rpcHost = services.AddRpcHost(ctx.HostInfo, ctx.Log);
                rpcHost.AddBackend<ITestPresences, TestPresences>();
            },
        }, @out)
{
    // Reproduces the prod presence freeze (2026-07-01): a value computed locally
    // on the shard's owner must be invalidated when the shard migrates to a newly
    // added node; otherwise it stays consistent-but-stale forever.
    [Fact]
    public async Task LocalShardMigrationMustInvalidateComputed()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var cancellationToken = cts.Token;
        var shardScheme = ShardScheme.TestBackend;
        var shardCount = shardScheme.ShardCount;

        await using var h1 = await NewAppHost();
        var o1 = h1.Services.ShardOwner(shardScheme);
        await ComputedTest.When(async ct => {
            var bits = await o1.BitmapState.Use(ct).ConfigureAwait(false);
            bits.SetBitCount().Should().Be(shardCount);
        }, TimeSpan.FromSeconds(15));

        var keys = new string?[shardCount];
        for (var i = 0; keys.Any(x => x is null); i++) {
            var key = $"key-{i.Format()}";
            keys[shardScheme.GetShardIndex(key)] ??= key;
        }

        var backend1 = h1.Services.GetRequiredService<ITestPresences>();
        var t0 = Moment.EpochStart + TimeSpan.FromDays(1);
        var computeds = new Computed<Moment?>[shardCount];
        for (var shard = 0; shard < shardCount; shard++) {
            var key = keys[shard]!;
            await backend1.CheckIn(key, t0, cancellationToken);
            var c = await Computed.Capture(() => backend1.GetLastCheckIn(key, cancellationToken), cancellationToken);
            c = await c.When(x => x == t0, cancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            computeds[shard] = c;
        }

        await using var h2 = await NewAppHost(o => o with { MustInitializeDb = false });
        var o2 = h2.Services.ShardOwner(shardScheme);
        await ComputedTest.When(async ct => {
            var bits1 = await o1.BitmapState.Use(ct).ConfigureAwait(false);
            var bits2 = await o2.BitmapState.Use(ct).ConfigureAwait(false);
            bits1.SetBitCount().Should().Be(shardCount / 2);
            bits2.SetBitCount().Should().Be(shardCount / 2);
        }, TimeSpan.FromSeconds(15));

        var migratedShards = new List<int>();
        for (var shard = 0; shard < shardCount; shard++)
            if (o2.GetShardStateComputed(shard, addDependency: false).Value.OwnershipStatus
                is ShardOwnershipStatus.OwnedByThisNode or ShardOwnershipStatus.MappedToThisNode)
                migratedShards.Add(shard);
        WriteLine($"Shards migrated to h2: {migratedShards.ToDelimitedString(",")}");

        var backend2 = h2.Services.GetRequiredService<ITestPresences>();
        var t1 = t0 + TimeSpan.FromHours(1);
        for (var shard = 0; shard < shardCount; shard++)
            await backend2.CheckIn(keys[shard]!, t1, cancellationToken);

        var frozenShards = new List<int>();
        for (var shard = 0; shard < shardCount; shard++) {
            var c = computeds[shard];
            try {
                await c.When(x => x == t1, cancellationToken)
                    .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                WriteLine($"Shard {shard.Format()} ({keys[shard]}): updated to t1");
            }
            catch (TimeoutException) {
                var isFrozen = c.IsConsistent();
                WriteLine($"Shard {shard.Format()} ({keys[shard]}): STALE, "
                    + $"IsConsistent={isFrozen}, Value={c.Value?.ToString() ?? "null"}");
                frozenShards.Add(shard);
            }
        }

        frozenShards.Should().BeEmpty(
            $"values of shards [{frozenShards.ToDelimitedString(",")}] must be invalidated "
            + $"after their shards migrated to h2 (migrated: [{migratedShards.ToDelimitedString(",")}])");
    }

    // The full prod scenario: node dies -> the survivor temporarily owns all shards
    // -> a new node comes up and takes half of them. Values computed on the survivor
    // (both local ones and replicas of the dead node's shards) must not freeze.
    // The root cause is the MeshRpcRoute ctor race: when a shard's local route
    // is created after the mesh maps the shard to this node, but before ShardOwner
    // sets MustOwn, no LocalExecutionAwaiter is set, so values computed via this route
    // get no ShardState dependency and never get invalidated. Fails in ~1/3 of runs.
    [Fact]
    public async Task TwoWaveRebalanceMustInvalidateComputed()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var cancellationToken = cts.Token;
        var shardScheme = ShardScheme.TestBackend;
        var shardCount = shardScheme.ShardCount;

        await using var h1 = await NewAppHost();
        var o1 = h1.Services.ShardOwner(shardScheme);
        var h2 = await NewAppHost(o => o with { MustInitializeDb = false });
        var o2 = h2.Services.ShardOwner(shardScheme);
        await ComputedTest.When(async ct => {
            var bits1 = await o1.BitmapState.Use(ct).ConfigureAwait(false);
            var bits2 = await o2.BitmapState.Use(ct).ConfigureAwait(false);
            bits1.SetBitCount().Should().Be(shardCount / 2);
            bits2.SetBitCount().Should().Be(shardCount / 2);
        }, TimeSpan.FromSeconds(15));

        var keys = new string?[shardCount];
        for (var i = 0; keys.Any(x => x is null); i++) {
            var key = $"tw-{i.Format()}";
            keys[shardScheme.GetShardIndex(key)] ??= key;
        }

        var h1Shards = new List<int>();
        for (var shard = 0; shard < shardCount; shard++)
            if (o1.GetShardStateComputed(shard, addDependency: false).Value.OwnershipStatus
                is ShardOwnershipStatus.OwnedByThisNode or ShardOwnershipStatus.MappedToThisNode)
                h1Shards.Add(shard);
        WriteLine($"Shards initially owned by h1 (survivor): {h1Shards.ToDelimitedString(",")}");

        // Check in + capture computeds on h1: local computeds for h1's shards,
        // RPC replicas for h2's shards - just like the survivor node in prod
        var backend1 = h1.Services.GetRequiredService<ITestPresences>();
        var t0 = Moment.EpochStart + TimeSpan.FromDays(1);
        var computeds = new Computed<Moment?>[shardCount];
        for (var shard = 0; shard < shardCount; shard++) {
            var key = keys[shard]!;
            await backend1.CheckIn(key, t0, cancellationToken);
            var c = await Computed.Capture(() => backend1.GetLastCheckIn(key, cancellationToken), cancellationToken);
            c = await c.When(x => x == t0, cancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            computeds[shard] = c;
        }

        // Wave 1: h2 dies -> h1 takes over all shards.
        // Recompute immediately, like live UI observers do - this maximizes the chance
        // to create the shard's local peer-ref inside the race window between
        // "mesh maps the shard to h1" and "ShardOwner sets MustOwn = true"
        await h2.DisposeAsync();
        for (var shard = 0; shard < shardCount; shard++) {
            var isRecomputed = false;
            while (!isRecomputed) {
                try {
                    computeds[shard] = await computeds[shard].When(x => x == t0, cancellationToken)
                        .WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
                    isRecomputed = true;
                }
                catch (RpcRerouteException) {
                    // The shard is migrating right now - retry
                }
            }
        }
        await ComputedTest.When(async ct => {
            var bits1 = await o1.BitmapState.Use(ct).ConfigureAwait(false);
            bits1.SetBitCount().Should().Be(shardCount);
        }, TimeSpan.FromSeconds(30));

        var rpcRefs = h1.Services.GetRequiredService<MeshRpcRefs>();
        for (var shard = 0; shard < shardCount; shard++) {
            var rpcRef = rpcRefs.Get(MeshRef.Shard(shardScheme, shard));
            WriteLine($"After wave 1: shard {shard.Format()}: route={rpcRef.Route}, "
                + $"hasLocalExecutionAwaiter={rpcRef.Route.LocalExecutionAwaiter is not null}");
        }

        // Wave 2: h3 comes up -> takes half of the shards from h1
        await using var h3 = await NewAppHost(o => o with { MustInitializeDb = false });
        var o3 = h3.Services.ShardOwner(shardScheme);
        await ComputedTest.When(async ct => {
            var bits1 = await o1.BitmapState.Use(ct).ConfigureAwait(false);
            var bits3 = await o3.BitmapState.Use(ct).ConfigureAwait(false);
            bits1.SetBitCount().Should().Be(shardCount / 2);
            bits3.SetBitCount().Should().Be(shardCount / 2);
        }, TimeSpan.FromSeconds(30));

        var migratedShards = new List<int>();
        for (var shard = 0; shard < shardCount; shard++)
            if (o3.GetShardStateComputed(shard, addDependency: false).Value.OwnershipStatus
                is ShardOwnershipStatus.OwnedByThisNode or ShardOwnershipStatus.MappedToThisNode)
                migratedShards.Add(shard);
        WriteLine($"Shards migrated to h3: {migratedShards.ToDelimitedString(",")}");

        for (var shard = 0; shard < shardCount; shard++) {
            var c = computeds[shard];
            var state1 = o1.GetShardStateComputed(shard, addDependency: false).Value.OwnershipStatus;
            WriteLine($"Before check-ins: shard {shard.Format()} ({keys[shard]}): "
                + $"IsConsistent={c.IsConsistent()}, h1 sees {state1}, "
                + $"{(migratedShards.Contains(shard) ? "migrated" : "stayed on h1")}, "
                + $"origin: {c.ToString(InvalidationSourceFormat.Origin)}");
        }

        var backend3 = h3.Services.GetRequiredService<ITestPresences>();
        var t1 = t0 + TimeSpan.FromHours(1);
        for (var shard = 0; shard < shardCount; shard++)
            await backend3.CheckIn(keys[shard]!, t1, cancellationToken);

        var frozenShards = new List<int>();
        for (var shard = 0; shard < shardCount; shard++) {
            var c = computeds[shard];
            try {
                await c.When(x => x == t1, cancellationToken)
                    .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                WriteLine($"Shard {shard.Format()} ({keys[shard]}): updated to t1");
            }
            catch (TimeoutException) {
                WriteLine($"Shard {shard.Format()} ({keys[shard]}): STALE, "
                    + $"IsConsistent={c.IsConsistent()}, Value={c.Value?.ToString() ?? "null"}");
                frozenShards.Add(shard);
            }
        }

        frozenShards.Should().BeEmpty(
            $"values of shards [{frozenShards.ToDelimitedString(",")}] must be invalidated "
            + $"after the two-wave rebalance (migrated to h3: [{migratedShards.ToDelimitedString(",")}])");
    }

    // Same as TwoWaveRebalanceMustInvalidateComputed, but repeats the kill-oldest/spawn-new
    // cycle several times to raise the chance of catching the race in a single run
    [Fact]
    public async Task MultiWaveRebalanceMustInvalidateComputed()
    {
        const int waveCount = 5;
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        var cancellationToken = cts.Token;
        var shardScheme = ShardScheme.TestBackend;
        var shardCount = shardScheme.ShardCount;

        var keys = new string?[shardCount];
        for (var i = 0; keys.Any(x => x is null); i++) {
            var key = $"mw-{i.Format()}";
            keys[shardScheme.GetShardIndex(key)] ??= key;
        }

        var hosts = new List<TestAppHost>();
        try {
            hosts.Add(await NewAppHost());
            hosts.Add(await NewAppHost(o => o with { MustInitializeDb = false }));
            var survivor = hosts[0]; // Computeds are captured and observed on this host, it never dies
            var oSurvivor = survivor.Services.ShardOwner(shardScheme);
            await WaitForBalance(hosts);

            var backend = survivor.Services.GetRequiredService<ITestPresences>();
            var t = Moment.EpochStart + TimeSpan.FromDays(1);
            var computeds = new Computed<Moment?>[shardCount];
            for (var shard = 0; shard < shardCount; shard++) {
                var key = keys[shard]!;
                await backend.CheckIn(key, t, cancellationToken);
                var c = await Computed.Capture(
                    () => backend.GetLastCheckIn(key, cancellationToken), cancellationToken);
                computeds[shard] = await c.When(x => x == t, cancellationToken)
                    .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            }

            for (var wave = 1; wave <= waveCount; wave++) {
                var survivorShards = await GetOwnedShards(oSurvivor);
                WriteLine($"--- Wave {wave.Format()}: survivor owns [{survivorShards.ToDelimitedString(",")}] ---");

                // Kill the other host, then recompute right away to hit the peer-ref creation race
                var dying = hosts[1];
                hosts.RemoveAt(1);
                await dying.DisposeAsync();
                for (var shard = 0; shard < shardCount; shard++) {
                    var isRecomputed = false;
                    while (!isRecomputed) {
                        try {
                            computeds[shard] = await computeds[shard].When(x => x == t, cancellationToken)
                                .WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
                            isRecomputed = true;
                        }
                        catch (RpcRerouteException) {
                            // The shard is migrating right now - retry
                        }
                    }
                }
                await ComputedTest.When(async ct => {
                    var bits = await oSurvivor.BitmapState.Use(ct).ConfigureAwait(false);
                    bits.SetBitCount().Should().Be(shardCount);
                }, TimeSpan.FromSeconds(30));

                // Spawn a new host -> half of the shards migrate to it
                var newcomer = await NewAppHost(o => o with { MustInitializeDb = false });
                hosts.Add(newcomer);
                await WaitForBalance(hosts);
                var newcomerShards = await GetOwnedShards(newcomer.Services.ShardOwner(shardScheme));
                WriteLine($"Wave {wave.Format()}: "
                    + $"shards [{newcomerShards.ToDelimitedString(",")}] migrated to the new host");

                // Check in through the newcomer, like prod clients connected to the new pod do;
                // the survivor's lazily started peer to the newcomer is a part of the repro
                var newcomerBackend = newcomer.Services.GetRequiredService<ITestPresences>();
                t += TimeSpan.FromHours(1);
                for (var shard = 0; shard < shardCount; shard++)
                    await newcomerBackend.CheckIn(keys[shard]!, t, cancellationToken);

                var frozenShards = new List<int>();
                for (var shard = 0; shard < shardCount; shard++) {
                    var c = computeds[shard];
                    try {
                        computeds[shard] = await c.When(x => x == t, cancellationToken)
                            .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                    }
                    catch (TimeoutException) {
                        var was = survivorShards.Contains(shard)
                            ? "LOCAL on survivor"
                            : "a replica of the dead host";
                        var became = newcomerShards.Contains(shard)
                            ? "migrated to the new host"
                            : "stayed on survivor";
                        WriteLine($"Wave {wave.Format()}, shard {shard.Format()} ({keys[shard]}): STALE, "
                            + $"IsConsistent={c.IsConsistent()}, Value={c.Value?.ToString() ?? "null"}, "
                            + $"was {was}, {became}");
                        frozenShards.Add(shard);
                    }
                }
                frozenShards.Should().BeEmpty(
                    $"wave {wave.Format()}: values of shards [{frozenShards.ToDelimitedString(",")}] "
                    + $"must be invalidated (migrated: [{newcomerShards.ToDelimitedString(",")}], "
                    + $"survivor-local before wave: [{survivorShards.ToDelimitedString(",")}])");
            }
        }
        finally {
            foreach (var host in hosts)
                await host.DisposeAsync();
        }
        return;

        async Task WaitForBalance(List<TestAppHost> currentHosts)
            => await ComputedTest.When(async ct => {
                foreach (var host in currentHosts) {
                    var owner = host.Services.ShardOwner(shardScheme);
                    var bits = await owner.BitmapState.Use(ct).ConfigureAwait(false);
                    bits.SetBitCount().Should().Be(shardCount / currentHosts.Count);
                }
            }, TimeSpan.FromSeconds(30));

        async Task<List<int>> GetOwnedShards(ShardOwner owner)
        {
            var bits = await owner.BitmapState.Use(cancellationToken).ConfigureAwait(false);
            var result = new List<int>();
            for (var shard = 0; shard < shardCount; shard++)
                if (bits[shard])
                    result.Add(shard);
            return result;
        }
    }
}

// Can't be nested, coz proxies aren't generated for nested types
[BackendService(nameof(HostRole.TestBackend), ServiceMode.Distributed)]
public interface ITestPresences : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<Moment?> GetLastCheckIn(string key, CancellationToken cancellationToken);
    Task CheckIn(string key, Moment at, CancellationToken cancellationToken);
}

public class TestPresences : ITestPresences
{
    // Static = a "database" shared by all test hosts in this process
    private static readonly ConcurrentDictionary<string, Moment> Store = new();

    [ComputeMethod]
    public virtual Task<Moment?> GetLastCheckIn(string key, CancellationToken cancellationToken)
        => Task.FromResult(Store.TryGetValue(key, out var at) ? (Moment?)at : null);

    public virtual Task CheckIn(string key, Moment at, CancellationToken cancellationToken)
    {
        Store[key] = at;
        using (Invalidation.Begin())
            _ = GetLastCheckIn(key, default);
        return Task.CompletedTask;
    }
}
