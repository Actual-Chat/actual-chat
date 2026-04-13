namespace ActualChat.Core.Server.UnitTests.Mesh;

/// <summary>
/// Proves that multiple renewal threads prevent lock expiry when individual
/// TryRenewBlocking calls are slow. With 1 thread, N locks × delay > renewal period
/// causes expiration. With enough threads, renewals complete in time.
/// </summary>
public class MeshLockRenewalThreadsTest(ITestOutputHelper @out) : TestBase(@out)
{
    /// <summary>
    /// 6 locks, each renewal takes 500ms, renewal period = 1.5s.
    /// 1 thread: cycle = 6 × 500ms = 3s > 1.5s → renewals can't keep up, locks expire.
    /// 3 threads: cycle = 6 × 500ms / 3 = 1s &lt; 1.5s → all locks survive.
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task MultipleThreadsPreventExpiry()
    {
        const int lockCount = 6;
        var renewalDelay = TimeSpan.FromMilliseconds(500);
        var expirationPeriod = TimeSpan.FromSeconds(5);
        var lockOptions = new MeshLockOptions(expirationPeriod: (float)expirationPeriod.TotalSeconds) {
            RenewalPeriodRatio = 0.3f, // 1.5s renewal period
        };
        var holdDuration = expirationPeriod * 2; // Hold for 10s — well beyond expiration if not renewed

        // --- Single thread: expect at least one lock to expire ---
        var singleThreadExpired = await RunWithThreadCount(1, lockCount, renewalDelay, lockOptions, holdDuration);
        Out.WriteLine($"Single thread: {singleThreadExpired}/{lockCount} locks expired");
        singleThreadExpired.Should().BeGreaterThan(0, "single thread should fail to renew all locks in time");

        // --- Multiple threads: all locks should survive ---
        var multiThreadExpired = await RunWithThreadCount(3, lockCount, renewalDelay, lockOptions, holdDuration);
        Out.WriteLine($"3 threads: {multiThreadExpired}/{lockCount} locks expired");
        multiThreadExpired.Should().Be(0, "3 threads should renew all locks in time");
    }

    private async Task<int> RunWithThreadCount(
        int threadCount,
        int lockCount,
        TimeSpan renewalDelay,
        MeshLockOptions lockOptions,
        TimeSpan holdDuration)
    {
        var services =  new ServiceCollection()
            .AddFusion()
            .Services
            .AddSingleton(new MeshLockRenewalThreads(threadCount))
            .BuildServiceProvider();

        var fakeLocks = new SlowMeshLocks(services, renewalDelay) {
            LockOptions = lockOptions,
        };

        var holders = new List<MeshLockHolder>();
        try {
            // Acquire all locks
            for (var i = 0; i < lockCount; i++) {
                var holder = await fakeLocks.TryLock($"key-{i}");
                holder.Should().NotBeNull($"lock key-{i} should be acquired");
                holders.Add(holder!);
            }

            // Hold for the duration, letting renewal threads work
            await Task.Delay(holdDuration);

            // Count how many expired
            return holders.Count(h => h.IsExpiredOnRenewal || h.StopToken.IsCancellationRequested);
        }
        finally {
            foreach (var h in holders)
                await h.DisposeAsync();
        }
    }

    /// <summary>
    /// Fake MeshLocks backend where TryRenewBlocking sleeps for a configurable duration,
    /// simulating slow Redis/K8s calls.
    /// </summary>
    private sealed class SlowMeshLocks(IServiceProvider services, TimeSpan renewalDelay)
        : MeshLocksBase(services)
    {
        private readonly ConcurrentDictionary<string, string> _held = new();

        public override string GetFullKey(string key) => $"test:{key}";

        public override Task<MeshLockInfo?> GetInfo(string key, CancellationToken cancellationToken = default)
            => Task.FromResult<MeshLockInfo?>(_held.TryGetValue(key, out var v) ? new MeshLockInfo(key, v) : null);

        public override Task<IAsyncSubscription<string>> Changes(string key, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public override Task<List<string>> ListKeys(string prefix, CancellationToken cancellationToken = default)
            => Task.FromResult(_held.Keys.Where(k => k.StartsWith(prefix)).ToList());

        public override IMeshLocks With(string keyPrefix, MeshLockOptions? lockOptions)
            => throw new NotSupportedException();

        protected override Task<bool> TryLock(string key, string value, TimeSpan expiresIn, CancellationToken cancellationToken)
        {
            var acquired = _held.TryAdd(key, value);
            return Task.FromResult(acquired);
        }

        protected override bool TryRenewBlocking(string key, string value, TimeSpan expiresIn, CancellationToken cancellationToken)
        {
            // Simulate slow backend call
            Thread.Sleep(renewalDelay);
            return _held.TryGetValue(key, out var v) && v == value;
        }

        protected override Task<MeshLockReleaseResult> TryRelease(string key, string value, CancellationToken cancellationToken)
        {
            var removed = _held.TryRemove(key, out var v) && v == value;
            return Task.FromResult(removed ? MeshLockReleaseResult.Released : MeshLockReleaseResult.AcquiredBySomeoneElse);
        }

        protected override Task<bool> ForceRelease(string key, bool mustNotify, CancellationToken cancellationToken)
        {
            var removed = _held.TryRemove(key, out _);
            return Task.FromResult(removed);
        }
    }
}
