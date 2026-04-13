using ActualChat.Testing.Host;
using ActualLab.Rpc;

namespace ActualChat.Core.Server.IntegrationTests.Sharding;

public class ShardLocalCacheTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(ShardLocalCacheTest)}", TestAppHostOptions.None, @out)
{
    [Fact(Timeout = 30_000)]
    public async Task GetOrAddCreatesEntry()
    {
        // arrange
        await using var host = await NewAppHost();
        var shardOwner = host.Services.ShardOwner(ShardScheme.TestBackend);

        var factoryCallCount = 0;
        var cache = new ShardLocalCache<string, string>(
            shardOwner,
            (key, _) => {
                Interlocked.Increment(ref factoryCallCount);
                return $"value-{key}";
            });

        // act — RequireShardOwnership inside GetOrAdd waits for ownership
        var value1 = await cache.GetOrAdd("test-key", addDependency: false, CancellationToken.None);
        var value2 = await cache.GetOrAdd("test-key", addDependency: false, CancellationToken.None);

        // assert
        value1.Should().Be("value-test-key");
        value2.Should().Be("value-test-key");
        factoryCallCount.Should().Be(1);
    }

    [Fact(Timeout = 30_000)]
    public async Task GetLocalReturnsExistingEntry()
    {
        // arrange
        await using var host = await NewAppHost();
        var shardOwner = host.Services.ShardOwner(ShardScheme.TestBackend);
        var cache = new ShardLocalCache<string, string>(
            shardOwner,
            (key, _) => $"value-{key}");

        // act & assert
        cache.GetLocal("test-key").Should().BeNull();
        await cache.GetOrAdd("test-key", addDependency: false, CancellationToken.None);
        cache.GetLocal("test-key").Should().Be("value-test-key");
    }

    [Fact(Timeout = 30_000)]
    public async Task TryRemoveEvictsAndCallsCallback()
    {
        // arrange
        await using var host = await NewAppHost();
        var shardOwner = host.Services.ShardOwner(ShardScheme.TestBackend);

        var evictedKeys = new List<string>();
        var cache = new ShardLocalCache<string, string>(
            shardOwner,
            (key, _) => $"value-{key}",
            onEvict: (key, _) => evictedKeys.Add(key));

        await cache.GetOrAdd("test-key", addDependency: false, CancellationToken.None);

        // act
        var removed = cache.TryRemove("test-key", out var value);

        // assert
        removed.Should().BeTrue();
        value.Should().Be("value-test-key");
        evictedKeys.Should().Equal(["test-key"]);
        cache.GetLocal("test-key").Should().BeNull();
    }

    [Fact(Timeout = 30_000)]
    public async Task ClearEvictsAllEntries()
    {
        // arrange
        await using var host = await NewAppHost();
        var shardOwner = host.Services.ShardOwner(ShardScheme.TestBackend);

        var evictedKeys = new List<string>();
        var cache = new ShardLocalCache<string, string>(
            shardOwner,
            (key, _) => $"value-{key}",
            onEvict: (key, _) => evictedKeys.Add(key));

        await cache.GetOrAdd("key-a", addDependency: false, CancellationToken.None);
        await cache.GetOrAdd("key-b", addDependency: false, CancellationToken.None);

        // act
        cache.Clear();

        // assert
        evictedKeys.Should().HaveCount(2);
        cache.GetLocal("key-a").Should().BeNull();
        cache.GetLocal("key-b").Should().BeNull();
    }

    [Fact(Timeout = 30_000)]
    public async Task DisposableValuesAreDisposedOnEviction()
    {
        // arrange
        await using var host = await NewAppHost();
        var shardOwner = host.Services.ShardOwner(ShardScheme.TestBackend);

        var disposed = false;
        var cache = new ShardLocalCache<string, DisposableValue>(
            shardOwner,
            (_, _) => new DisposableValue(() => disposed = true));

        await cache.GetOrAdd("test-key", addDependency: false, CancellationToken.None);

        // act
        cache.TryRemove("test-key", out _);

        // assert
        disposed.Should().BeTrue();
    }

    [Fact(Timeout = 30_000)]
    public async Task OnEvictAndDisposableAreBothCalled()
    {
        // arrange
        await using var host = await NewAppHost();
        var shardOwner = host.Services.ShardOwner(ShardScheme.TestBackend);

        var onEvictCalled = false;
        var disposed = false;
        var cache = new ShardLocalCache<string, DisposableValue>(
            shardOwner,
            (_, _) => new DisposableValue(() => disposed = true),
            onEvict: (_, _) => onEvictCalled = true);

        await cache.GetOrAdd("test-key", addDependency: false, CancellationToken.None);

        // act
        cache.TryRemove("test-key", out _);

        // assert
        onEvictCalled.Should().BeTrue();
        disposed.Should().BeTrue();
    }

    [Fact(Timeout = 60_000)]
    public async Task StaleEntryIsEvictedOnAccess()
    {
        // arrange
        await using var h1 = await NewAppHost();
        var o1 = h1.Services.ShardOwner(ShardScheme.TestBackend);

        var factoryCallCount = 0;
        var evictedValues = new List<string>();
        var cache = new ShardLocalCache<string, string>(
            o1,
            (key, ownership) => {
                Interlocked.Increment(ref factoryCallCount);
                return $"value-{key}-v{factoryCallCount}";
            },
            onEvict: (_, value) => evictedValues.Add(value));

        // Populate cache entry — GetOrAdd waits for ownership
        var key = "stale-test-key";
        var value1 = await cache.GetOrAdd(key, addDependency: false, CancellationToken.None);
        factoryCallCount.Should().Be(1);
        var shardIndex = o1.ShardScheme.GetShardIndex(key);

        // Start second host, causing shard redistribution
        await using var h2 = await NewAppHost(o => o with { MustInitializeDb = false });
        var o2 = h2.Services.ShardOwner(ShardScheme.TestBackend);

        // Wait for redistribution to complete
        await ComputedTest.When(async ct => {
            var bits1 = await o1.BitmapState.Use(ct).ConfigureAwait(false);
            var bits2 = await o2.BitmapState.Use(ct).ConfigureAwait(false);
            bits1.SetBitCount().Should().Be(ShardScheme.TestBackend.ShardCount / 2);
            bits2.SetBitCount().Should().Be(ShardScheme.TestBackend.ShardCount / 2);
        }, TimeSpan.FromSeconds(20));

        // Check if key's shard is still owned by h1
        var state = o1.States[shardIndex].Value;
        if (state.OwnershipStatus != ShardOwnershipStatus.OwnedByThisNode) {
            // Shard moved to h2 — cache should throw RpcRerouteException
            Func<Task> act = async () => await cache.GetOrAdd(key, addDependency: false, CancellationToken.None);
            await act.Should().ThrowAsync<RpcRerouteException>();
            WriteLine("Shard moved to h2 — RpcRerouteException thrown as expected");
        }
        else {
            // Shard stayed with h1 — verify same ownership instance means no re-creation
            var value2 = await cache.GetOrAdd(key, addDependency: false, CancellationToken.None);
            if (ReferenceEquals(state.Ownership, o1.States[shardIndex].Value.Ownership)) {
                // Ownership instance unchanged — factory should NOT have been called again
                factoryCallCount.Should().Be(1);
                value2.Should().Be(value1);
                WriteLine("Shard stayed with h1, same ownership — entry reused");
            }
            else {
                // Ownership re-acquired — factory SHOULD have been called again
                factoryCallCount.Should().Be(2);
                evictedValues.Should().Contain(value1);
                WriteLine("Shard stayed with h1, new ownership — entry recreated");
            }
        }
    }

    [Fact(Timeout = 60_000)]
    public async Task EvictionLoopRemovesStaleEntries()
    {
        // arrange
        await using var h1 = await NewAppHost();
        var o1 = h1.Services.ShardOwner(ShardScheme.TestBackend);

        var evictedKeys = new ConcurrentBag<string>();
        var cache = new ShardLocalCache<string, string>(
            o1,
            (key, _) => $"value-{key}",
            onEvict: (key, _) => evictedKeys.Add(key),
            evictionInterval: TimeSpan.FromMilliseconds(100));

        // Populate entries across multiple shards
        var populatedKeys = new List<string>();
        for (var i = 0; i < 20; i++) {
            var key = $"evict-key-{i}";
            try {
                await cache.GetOrAdd(key, addDependency: false, CancellationToken.None);
                populatedKeys.Add(key);
            }
            catch (RpcRerouteException) {
                // Skip keys whose shards aren't owned
            }
        }
        populatedKeys.Should().NotBeEmpty();

        // Start eviction loop
        using var cts = new CancellationTokenSource();
        var evictionTask = cache.RunEvictionLoop(cts.Token);

        // Start second host to trigger shard ownership changes
        await using var h2 = await NewAppHost(o => o with { MustInitializeDb = false });
        var o2 = h2.Services.ShardOwner(ShardScheme.TestBackend);

        // Wait for redistribution
        await ComputedTest.When(async ct => {
            var bits1 = await o1.BitmapState.Use(ct).ConfigureAwait(false);
            var bits2 = await o2.BitmapState.Use(ct).ConfigureAwait(false);
            bits1.SetBitCount().Should().Be(ShardScheme.TestBackend.ShardCount / 2);
            bits2.SetBitCount().Should().Be(ShardScheme.TestBackend.ShardCount / 2);
        }, TimeSpan.FromSeconds(20));

        // Wait for eviction loop to run a few cycles
        await Task.Delay(1000);
        await cts.CancelAsync();
        await evictionTask.SilentAwait(false);

        // assert — evicted entries should no longer be in cache
        WriteLine($"Populated: {populatedKeys.Count}, Evicted: {evictedKeys.Count}");
        foreach (var key in evictedKeys)
            cache.GetLocal(key).Should().BeNull();
    }

    // Nested types

    private sealed class DisposableValue(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
