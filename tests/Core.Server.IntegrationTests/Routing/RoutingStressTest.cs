using System.Collections.Concurrent;
using ActualChat.Attributes;
using ActualChat.Hosting;
using ActualChat.Testing.Host;
using ActualLab.Rpc;

namespace ActualChat.Core.Server.IntegrationTests.Routing;

// Register a faster mesh lock preset for stress tests
static file class MeshLockOptionsRegistration
{
    static MeshLockOptionsRegistration()
    {
        var presets = (Dictionary<string, MeshLockOptions>)MeshLockOptions.Presets;
        presets["Fast"] = new MeshLockOptions(expirationPeriod: 5, renewalPeriodRatio: 0.4f) {
            UnconditionalCheckPeriod = TimeSpan.FromSeconds(1),
        };
    }

    public static void EnsureRegistered() { } // Call to trigger static constructor
}

/// <summary>
/// Stress test for RPC routing with distributed compute services.
/// This test creates and destroys nodes rapidly while calling compute methods
/// to verify that routing works correctly and no "RemoteComputeMethodCallFromTheSameService" errors occur.
/// </summary>
[Collection(nameof(ServerCollection))]
[Trait("Category", "Slow")]
public class RoutingStressTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(RoutingStressTest)}", TestAppHostOptions.None with {
        MustStart = true,
        ConfigureServices = (ctx, services) => {
            var rpcHost = services.AddRpcHost(ctx.HostInfo);
            // Register our test service as a backend service in Distributed mode
            rpcHost.AddBackend<IRoutingTestService, RoutingTestService>();
        },
    }, @out)
{
    private volatile int _errorCount;

    /// <summary>
    /// Basic test that verifies compute method routing between two hosts.
    /// </summary>
    [Fact(Timeout = 60_000)]
    public async Task BasicRoutingTest()
    {
        var shardScheme = ShardScheme.TestBackend;

        // Create first host
        await using var h1 = await NewAppHost();
        var w1 = h1.Services.GetRequiredService<MeshWatcher>();
        var s1 = h1.Services.GetRequiredService<IRoutingTestService>();
        var o1 = h1.Services.ShardOwner(shardScheme);

        await w1.WhenAnnounced;
        WriteLine($"Host1 announced: {w1.ThisNode.Ref}");

        // Set a value on host1
        var key = "test-key";
        var value1 = "value-from-h1";
        await s1.SetValue(ShardKey.New(0), key, value1);
        var result = await s1.GetValue(ShardKey.New(0), key);
        result.Value.Should().Be(value1);
        result.HostNodeRef.Should().Be(w1.ThisNode.Ref.Value);
        WriteLine($"Set and got value on h1: {result.Value} from {result.HostNodeRef}");

        // Create second host
        await using var h2 = await NewAppHost();
        var w2 = h2.Services.GetRequiredService<MeshWatcher>();
        var s2 = h2.Services.GetRequiredService<IRoutingTestService>();
        var o2 = h2.Services.ShardOwner(shardScheme);

        await w2.WhenAnnounced;
        WriteLine($"Host2 announced: {w2.ThisNode.Ref}");

        // Wait for both hosts to see each other
        var syncTimeout = TimeSpan.FromSeconds(10);
        await w1.State.Computed.When(x => x.AllNodes.Count >= 2).WaitAsync(syncTimeout);
        await w2.State.Computed.When(x => x.AllNodes.Count >= 2).WaitAsync(syncTimeout);
        WriteLine($"Both hosts see each other: h1 sees {w1.State.Value.AllNodes.Count}, h2 sees {w2.State.Value.AllNodes.Count}");

        // Wait for shard redistribution
        await ComputedTest.When(async ct => {
            var bits1 = await o1.BitmapState.Use(ct).ConfigureAwait(false);
            var bits2 = await o2.BitmapState.Use(ct).ConfigureAwait(false);
            var count1 = bits1.SetBitCount();
            var count2 = bits2.SetBitCount();
            WriteLine($"Shard distribution: h1={count1}, h2={count2}");
            (count1 + count2).Should().BeGreaterThanOrEqualTo(shardScheme.ShardCount);
        }, TimeSpan.FromSeconds(15));

        // Value should still be accessible (may route to h1 or h2 depending on shard ownership)
        var result2 = await s2.GetValue(ShardKey.New(0), key);
        // Note: The value might be empty if shard 0 moved to h2 (which has empty storage)
        WriteLine($"Got value from h2's perspective: {result2.Value} from {result2.HostNodeRef}");

        // Test value on different shard key
        var key2 = "key2";
        var value2 = "value2";
        await s2.SetValue(ShardKey.New(1), key2, value2);
        var result3 = await s1.GetValue(ShardKey.New(1), key2);
        result3.Value.Should().Be(value2);
        WriteLine($"Cross-host routing works: got '{result3.Value}' from {result3.HostNodeRef}");
    }

    /// <summary>
    /// Stress test that rapidly creates and destroys hosts while making compute method calls.
    /// This test verifies that routing handles topology changes correctly.
    /// </summary>
    [Fact(Timeout = 120_000, Skip = "Run it manually")]
    public async Task RapidTopologyChangeStressTest()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(100));
        var cancellationToken = cts.Token;

        var workerCount = TestRunnerInfo.IsBuildAgent() ? 3 : 10;
        var callsPerWorker = TestRunnerInfo.IsBuildAgent() ? 20 : 50;
        var startDelays = new RandomTimeSpan(2, 0.5);
        var runDelays = new RandomTimeSpan(3, 1);

        WriteLine($"Starting stress test with {workerCount} workers, {callsPerWorker} calls each");

        _errorCount = 0;
        var tasks = Enumerable.Range(0, workerCount)
            .Select(i => HostWorker(i, callsPerWorker, startDelays, runDelays, cancellationToken))
            .ToList();

        await Task.WhenAll(tasks);

        _errorCount.Should().Be(0, "No routing errors should occur during topology changes");
        WriteLine($"Stress test completed. Total errors: {_errorCount}");
    }

    /// <summary>
    /// Test that verifies compute method invalidation works correctly during rerouting.
    /// </summary>
    [Fact(Timeout = 60_000)]
    public async Task ComputedInvalidationOnRerouteTest()
    {
        var syncTimeout = TimeSpan.FromSeconds(10);

        // Create first host
        await using var h1 = await NewAppHost();
        var w1 = h1.Services.GetRequiredService<MeshWatcher>();
        var s1 = h1.Services.GetRequiredService<IRoutingTestService>();

        await w1.WhenAnnounced;

        // Capture a computed value
        var key = "invalidation-test";
        await s1.SetValue(ShardKey.New(0), key, "initial");
        var computed = await Computed.Capture(() => s1.GetValue(ShardKey.New(0), key));
        computed.Value.Value.Should().Be("initial");
        var initialHostRef = computed.Value.HostNodeRef;
        WriteLine($"Initial computed from {initialHostRef}: {computed.Value.Value}");

        // Create second host to cause shard redistribution
        await using var h2 = await NewAppHost();
        var w2 = h2.Services.GetRequiredService<MeshWatcher>();
        var s2 = h2.Services.GetRequiredService<IRoutingTestService>();

        await w2.WhenAnnounced;
        await w1.State.Computed.When(x => x.AllNodes.Count >= 2).WaitAsync(syncTimeout);
        await w2.State.Computed.When(x => x.AllNodes.Count >= 2).WaitAsync(syncTimeout);
        WriteLine($"Host2 joined: {w2.ThisNode.Ref}");

        // Update value on h2 for the same key (different shard)
        await s2.SetValue(ShardKey.New(1), key, "updated");

        // The computed should eventually update (either same value or rerouted)
        // Note: If shard 0 moves to h2, the value will be empty because h2's storage is empty.
        // We verify that the computed mechanism works (gets a response), not that the value persists.
        await ComputedTest.When(async ct => {
            var v = await computed.Use(ct);
            WriteLine($"Computed update check: {v.Value} from {v.HostNodeRef}");
            // Verify we get a valid response - if still on h1, value is "initial"; if rerouted to h2, value may be empty
            if (v.HostNodeRef == initialHostRef)
                v.Value.Should().Be("initial");
            // If rerouted to h2, empty is valid (h2's storage is empty)
        }, TimeSpan.FromSeconds(10));

        // Dispose h1 to force rerouting
        WriteLine("Disposing h1 to test rerouting...");
        await h1.DisposeAsync();

        // Wait for h2 to detect h1 is gone
        await w2.State.Computed.When(x => x.LiveNodes.Length == 1).WaitAsync(syncTimeout);
        WriteLine("H2 detected h1 is gone");

        // Now calling s2 should work and return values from h2
        var result = await s2.GetValue(ShardKey.New(0), key);
        result.HostNodeRef.Should().Be(w2.ThisNode.Ref.Value);
        WriteLine($"After h1 disposal, got value from {result.HostNodeRef}: {result.Value}");
    }

    /// <summary>
    /// Test concurrent calls during rapid host addition.
    /// </summary>
    [Fact(Timeout = 90_000, Skip = "Run it manually")]
    public async Task ConcurrentCallsDuringHostAdditionTest()
    {
        MeshLockOptionsRegistration.EnsureRegistered();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(80));
        var cancellationToken = cts.Token;
        var perCallTimeout = TimeSpan.FromSeconds(10);

        // Start with one host using faster mesh lock options
        await using var h1 = await NewAppHost(o => o with { MeshLockOptionsPreset = "Fast" });
        var w1 = h1.Services.GetRequiredService<MeshWatcher>();
        var s1 = h1.Services.GetRequiredService<IRoutingTestService>();
        await w1.WhenAnnounced;

        var callCount = TestRunnerInfo.IsBuildAgent() ? 50 : 200;
        var addedHostCount = 0;
        var completedCalls = 0;
        var errors = new ConcurrentBag<Exception>();

        // Start making calls
        var callTask = Task.Run(async () => {
            var rnd = new Random();
            for (var i = 0; i < callCount && !cancellationToken.IsCancellationRequested; i++) {
                try {
                    var shardKey = ShardKey.New(rnd.Next(0, 10));
                    var key = $"call-{i}";
                    var value = $"value-{i}";

                    // Use per-call timeout to prevent indefinite blocking during topology changes
                    await s1.SetValue(shardKey, key, value).WaitAsync(perCallTimeout, cancellationToken);
                    var result = await s1.GetValue(shardKey, key).WaitAsync(perCallTimeout, cancellationToken);

                    if (result.Value != value)
                        errors.Add(new InvalidOperationException($"Value mismatch: expected {value}, got {result.Value}"));

                    Interlocked.Increment(ref completedCalls);

                    if (i % 10 == 0)
                        WriteLine($"Completed {completedCalls} calls, hosts added: {addedHostCount}");

                    await Task.Delay(10, cancellationToken);
                }
                catch (TimeoutException e) {
                    errors.Add(e);
                    WriteLine($"Call {i} timed out: {e.Message}");
                }
                catch (Exception e) when (e is not OperationCanceledException) {
                    errors.Add(e);
                    WriteLine($"Call {i} failed: {e.GetType().Name}: {e.Message}");
                }
            }
        }, cancellationToken);

        // Concurrently add hosts
        var hosts = new ConcurrentBag<TestAppHost>();
        var addHostTask = Task.Run(async () => {
            var rnd = new Random();
            while (!callTask.IsCompleted && !cancellationToken.IsCancellationRequested) {
                await Task.Delay(rnd.Next(100, 500), cancellationToken);

                var host = await NewAppHost(o => o with { MeshLockOptionsPreset = "Fast" });
                hosts.Add(host);
                Interlocked.Increment(ref addedHostCount);

                var watcher = host.Services.GetRequiredService<MeshWatcher>();
                await watcher.WhenAnnounced;
                WriteLine($"Added host {addedHostCount}: {watcher.ThisNode.Ref}");
            }
        }, cancellationToken);

        await callTask;
        await cts.CancelAsync(); // Stop adding hosts
        await addHostTask.SilentAwait();

        // Cleanup hosts in parallel
        await Task.WhenAll(hosts.Select(h => h.DisposeAsync().AsTask()));

        errors.Should().BeEmpty($"No errors should occur during concurrent operations. Errors: {string.Join("; ", errors.Take(5).Select(e => e.Message))}");
        WriteLine($"Test completed: {completedCalls} calls, {addedHostCount} hosts added, {errors.Count} errors");
    }

    private async Task HostWorker(
        int index,
        int callCount,
        RandomTimeSpan startDelays,
        RandomTimeSpan runDelays,
        CancellationToken cancellationToken)
    {
        var workerName = $"worker-{index}";

        void Log(string message) => WriteLine($"{workerName}: {message}");

        await Task.Delay(startDelays.Next(), cancellationToken);
        Log("Starting host...");

        await using var host = await NewAppHost(); // Only first worker initializes DB

        var watcher = host.Services.GetRequiredService<MeshWatcher>();
        var service = host.Services.GetRequiredService<IRoutingTestService>();

        await watcher.WhenAnnounced;
        Log($"Host announced: {watcher.ThisNode.Ref}");

        var rnd = new Random();
        var localErrors = 0;

        for (var i = 0; i < callCount && !cancellationToken.IsCancellationRequested; i++) {
            try {
                var shardKey = ShardKey.New(rnd.Next(0, ShardScheme.TestBackend.ShardCount));
                var key = $"{workerName}-key-{i}";
                var value = $"{workerName}-value-{i}";

                // Test SetValue + GetValue
                await service.SetValue(shardKey, key, value);
                var result = await service.GetValue(shardKey, key);

                if (result.Value != value) {
                    localErrors++;
                    Log($"[ERROR] Call {i}: value mismatch, expected '{value}', got '{result.Value}' from {result.HostNodeRef}");
                }

                // Small delay between calls
                await Task.Delay(rnd.Next(5, 20), cancellationToken);
            }
            catch (RpcRerouteException e) {
                // Rerouting is expected during topology changes
                Log($"Call {i}: rerouting (expected) - {e.Message}");
            }
            catch (InvalidOperationException e) when (e.Message.Contains("RemoteComputeMethodCallFromTheSameService")) {
                // This is the error we're specifically testing for - it should NOT happen
                localErrors++;
                Interlocked.Increment(ref _errorCount);
                Log($"[CRITICAL ERROR] Call {i}: RemoteComputeMethodCallFromTheSameService - {e.Message}");
            }
            catch (Exception e) when (e is not OperationCanceledException) {
                localErrors++;
                Log($"[ERROR] Call {i}: {e.GetType().Name} - {e.Message}");
            }
        }

        Interlocked.Add(ref _errorCount, localErrors);

        await Task.Delay(runDelays.Next(), cancellationToken);
        Log($"Stopping. Made {callCount} calls, {localErrors} errors");
    }
}

// Test service interface
[BackendService(nameof(HostRole.TestBackend), ServiceMode.Distributed)]
public interface IRoutingTestService : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<RoutingTestValue> GetValue(ShardKey shardKey, string key, CancellationToken cancellationToken = default);

    Task SetValue(ShardKey shardKey, string key, string value, CancellationToken cancellationToken = default);
}

// Value type returned by the test service
[DataContract]
public sealed partial record RoutingTestValue(
    [property: DataMember(Order = 0)] string Value,
    [property: DataMember(Order = 1)] string HostNodeRef
);

// Test service implementation
public class RoutingTestService(IServiceProvider services) : IRoutingTestService
{
    private readonly ConcurrentDictionary<string, string> _storage = new();
    private readonly MeshWatcher _meshWatcher = services.MeshWatcher();
    private readonly ShardOwner _shardOwner = services.ShardOwner(ShardScheme.TestBackend);

    public virtual async Task<RoutingTestValue> GetValue(ShardKey shardKey, string key, CancellationToken cancellationToken = default)
    {
        // Ensure we own this shard
        await _shardOwner.RequireShardOwnership(shardKey, addDependency: true, cancellationToken).ConfigureAwait(false);

        var value = _storage.GetValueOrDefault(key, "");
        return new RoutingTestValue(value, _meshWatcher.ThisNode.Ref.Value);
    }

    public virtual async Task SetValue(ShardKey shardKey, string key, string value, CancellationToken cancellationToken = default)
    {
        // Ensure we own this shard
        await _shardOwner.RequireShardOwnership(shardKey, addDependency: false, cancellationToken).ConfigureAwait(false);

        _storage[key] = value;

        // Invalidate the computed for this key
        using (Invalidation.Begin())
            _ = GetValue(shardKey, key, cancellationToken);
    }
}
