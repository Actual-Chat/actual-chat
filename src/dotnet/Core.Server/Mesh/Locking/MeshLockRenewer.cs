namespace ActualChat.Mesh;

/// <summary>
/// Spawns N dedicated OS threads to renew all <see cref="MeshLockHolder"/> instances.
/// These threads are immune to ThreadPool starvation, which can cause lock expiry
/// under heavy async load. Multiple threads ensure renewals complete on time even
/// when individual Redis/K8s calls are slow.
/// </summary>
public sealed class MeshLockRenewer : IDisposable
{
    private readonly ConcurrentDictionary<MeshLockHolder, RenewalEntry> _entries = new();
    private readonly SemaphoreSlim _wakeSemaphore = new(0, 1);
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread[] _threads;

    public MeshLockRenewer(int threadCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(threadCount, 1);

        _threads = new Thread[threadCount];
        for (var i = 0; i < threadCount; i++) {
            var threadIndex = i;
            var thread = new Thread(() => ThreadStart(threadIndex)) {
                IsBackground = true,
                Name = $"MeshLockRenewer-{i}",
                Priority = ThreadPriority.AboveNormal,
            };
            _threads[i] = thread;
            thread.Start();
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        TryWakeUp();
        foreach (var thread in _threads)
            thread.Join(TimeSpan.FromSeconds(5));
        _cts.Dispose();
    }

    public ThreadRegistration Register(MeshLockHolder holder)
    {
        var entry = new RenewalEntry {
            NextRenewalAt = holder.Clock.Now + holder.Options.RenewalPeriod,
        };
        _entries[holder] = entry;
        // Wake threads when the holder is stopped so entries are cleaned up promptly
        var wakeOnStop = holder.StopToken.Register(TryWakeUp);
        TryWakeUp();
        return new ThreadRegistration(this, holder, entry.ExpiredTcs.Task, wakeOnStop);
    }

    // Private methods

    private void Unregister(MeshLockHolder holder)
    {
        if (_entries.TryRemove(holder, out var entry))
            entry.ExpiredTcs.TrySetCanceled();
    }

    private void TryWakeUp()
    {
        try {
            _wakeSemaphore.Release();
        }
        catch (SemaphoreFullException) {
            // Already signaled
        }
    }

    private void ThreadStart(int threadIndex)
    {
        var ct = _cts.Token;
        while (!ct.IsCancellationRequested) {
            var minSleepMs = 1000;

            // No ToArray() needed: ConcurrentDictionary enumeration is thread-safe,
            // and each entry is independently claimed via CAS, so mid-iteration changes are harmless.
            foreach (var (holder, entry) in _entries) {
                try {
                    if (entry.ExpiredTcs.Task.IsCompleted) {
                        _entries.TryRemove(holder, out _);
                        continue;
                    }

                    var stopCt = holder.StopToken;
                    if (stopCt.IsCancellationRequested) {
                        _entries.TryRemove(holder, out _);
                        entry.ExpiredTcs.TrySetCanceled(stopCt);
                        continue;
                    }

                    // Check if it's time to renew
                    var now = holder.Clock.Now;
                    var remaining = entry.NextRenewalAt - now;

                    if (remaining > TimeSpan.Zero) {
                        var remainingMs = (int)remaining.TotalMilliseconds;
                        if (remainingMs < minSleepMs)
                            minSleepMs = remainingMs;
                        continue;
                    }

                    // Try to claim this entry for renewal (atomic, prevents double-processing)
                    if (Interlocked.CompareExchange(ref entry.ClaimedByThread, threadIndex, -1) != -1)
                        continue; // Another thread already claimed it

                    // Re-check: another thread may have already renewed while we waited
                    var nowAfterClaim = holder.Clock.Now;
                    if (entry.NextRenewalAt > nowAfterClaim) {
                        Volatile.Write(ref entry.ClaimedByThread, -1);
                        continue;
                    }

                    // Re-check: holder may have been stopped between the earlier check and now
                    if (stopCt.IsCancellationRequested) {
                        Volatile.Write(ref entry.ClaimedByThread, -1);
                        _entries.TryRemove(holder, out _);
                        entry.ExpiredTcs.TrySetCanceled(stopCt);
                        continue;
                    }
                    try {
                        var isHeld = holder.TryRenewBlocking(stopCt);
                        if (!isHeld) {
                            _entries.TryRemove(holder, out _);
                            entry.ExpiredTcs.TrySetResult();
                            continue;
                        }
                        entry.FailureCount = 0;
                        entry.NextRenewalAt = holder.Clock.Now + holder.Options.RenewalPeriod;
                    }
                    catch (OperationCanceledException) {
                        _entries.TryRemove(holder, out _);
                        entry.ExpiredTcs.TrySetCanceled();
                        continue;
                    }
                    catch (Exception e) {
                        // Transient failure — schedule retry with backoff
                        entry.FailureCount++;
                        var retryDelay = holder._backend.RetryDelays[entry.FailureCount];
                        var retryNow = holder.Clock.Now;
                        var deadline = holder.ExpiresAt - holder.Options.ExpirationSafetyMargin;
                        if (retryNow + retryDelay >= deadline) {
                            holder.Log?.LogError(e,
                                "[+*] {Key}: renewal failed, no time to retry",
                                holder.FullKey);
                            _entries.TryRemove(holder, out _);
                            entry.ExpiredTcs.TrySetResult();
                        }
                        else {
                            holder.Log?.LogWarning(e,
                                "[+*] {Key}: renewal failed, will retry in {Delay}",
                                holder.FullKey, retryDelay);
                            entry.NextRenewalAt = retryNow + retryDelay;
                        }
                    }
                    finally {
                        Volatile.Write(ref entry.ClaimedByThread, -1);
                    }
                    continue;
                }
                catch (Exception ex) {
                    // Prevent one bad entry from crashing the thread and orphaning all others.
                    // Use TrySetCanceled (not TrySetResult) to signal clean shutdown rather than
                    // false expiration, which would trigger host termination via MeshWatcher.
                    _entries.TryRemove(holder, out _);
                    entry.ExpiredTcs.TrySetCanceled();
                    try {
                        holder.Log?.LogWarning(ex,
                            "[+*] {Key}: renewal entry removed due to unexpected error",
                            holder.FullKey);
                    }
                    catch {
                        // Logger might be from a disposed service provider
                    }
                }
            }
            try {
                _wakeSemaphore.Wait(Math.Max(10, minSleepMs), ct);
            }
            catch (OperationCanceledException) {
                // Disposal — exit loop
            }
        }
    }

    // Nested types

    public readonly struct ThreadRegistration : IDisposable
    {
        private readonly MeshLockRenewer _threads;
        private readonly MeshLockHolder _holder;
        private readonly Task _expiredTask;
        private readonly CancellationTokenRegistration _wakeOnStop;

        internal ThreadRegistration(
            MeshLockRenewer threads,
            MeshLockHolder holder,
            Task expiredTask,
            CancellationTokenRegistration wakeOnStop)
        {
            _threads = threads;
            _holder = holder;
            _expiredTask = expiredTask;
            _wakeOnStop = wakeOnStop;
        }

        /// <summary>
        /// Waits until the lock expires or the cancellation token fires.
        /// Returns true if the lock expired, false if cancelled (normal shutdown).
        /// </summary>
        public async Task<bool> WhenExpired(CancellationToken cancellationToken)
        {
            // Use Task.WhenAny instead of WaitAsync to avoid issues with
            // CancellationTokenSource disposal breaking WaitAsync's internal registration
            var cancelSource = AsyncTaskMethodBuilderExt.New();
            var registration = cancellationToken.Register(
                static state => ((AsyncTaskMethodBuilder)state!).TrySetResult(),
                cancelSource);
            await using var _ = registration.ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
                return false;

            var completedTask = await Task.WhenAny(_expiredTask, cancelSource.Task).ConfigureAwait(false);
            return completedTask == _expiredTask && _expiredTask.IsCompletedSuccessfully;
        }

        public void Dispose()
        {
            _wakeOnStop.Dispose();
            _threads.Unregister(_holder);
        }
    }

    private sealed class RenewalEntry
    {
        public readonly TaskCompletionSource ExpiredTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Moment NextRenewalAt;
        public int FailureCount;
        public int ClaimedByThread = -1;
    }
}
