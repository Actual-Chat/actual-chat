namespace ActualChat.Mesh;

/// <summary>
/// Maintains a distributed lock with automatic renewal and dependency tracking.
/// </summary>
public class MeshLockHolder : WorkerBase, IHasId<string>
{
    private readonly IMeshLocksBackend _backend;
    private HashSet<Task>? _dependencies;
    private long _expiresAtTicks;

    private MomentClock Clock => _backend.Clock;
    private ILogger? Log => _backend.Log;
    private ILogger? DebugLog => _backend.DebugLog;

    public string Id { get; } // This is the ID of the lock holder, i.e., this object
    public string Key { get; }
    public string FullKey { get; }
    public MeshLockOptions Options { get; }
    public Moment CreatedAt { get; }
    public Moment ExpiresAt {
        get => new(Interlocked.Read(ref _expiresAtTicks));
        protected set => Interlocked.Exchange(ref _expiresAtTicks, value.EpochOffsetTicks);
    }
    public bool IsExpiredOnRenewal { get; protected set; }

    public MeshLockHolder(
        IMeshLocksBackend backend,
        string id,
        string key,
        MeshLockOptions options,
        CancellationToken cancellationToken)
        : base(cancellationToken.CreateLinkedTokenSource())
    {
        if (key.IsNullOrEmpty())
            throw new ArgumentOutOfRangeException(nameof(key));

        options.RequireValid();

        _backend = backend;
        Id = id;
        Key = key;
        FullKey = backend.GetFullKey(key);
        Options = options;
        CreatedAt = Clock.Now;
    }

    public Task AddDependency(Func<CancellationToken, Task> dependencyFactory, bool autoRemove = true)
    {
        Task dependency;
        lock (Lock) {
            StopToken.ThrowIfCancellationRequested();
            var dependencies = _dependencies ??= new ();
            dependency = dependencyFactory.Invoke(StopToken);
            dependencies.Add(dependency);
        }
        if (autoRemove)
            _ = dependency.ContinueWith(RemoveDependency, TaskScheduler.Default);
        return dependency;
    }

    public void RemoveDependency(Task dependency)
    {
        lock (Lock)
            _dependencies?.Remove(dependency);
    }

    // Protected methods

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var now = Clock.Now;
        ExpiresAt = now + Options.ExpirationPeriod;
        DebugLog?.LogDebug(
            "[+] {Key}: acquired in {AcquireTime}, value = {StoredValue}",
            FullKey, (now - CreatedAt).ToShortString(), Id);

        var chaosMaker = _backend.ChaosMaker;
        if (chaosMaker.IsEnabled)
            _ = Task.Run(async () => {
                try {
                    await chaosMaker.Act(this, cancellationToken).ResultAwait();
                }
                catch (Exception e) {
                    if (e.IsCancellationOf(cancellationToken))
                        return;

                    Log?.LogWarning("[!] {Key}: ChaosMaker-caused termination", FullKey);
                    _ = Stop();
                }
            }, CancellationToken.None);

        // Register with shared renewal thread — single dedicated OS thread for all locks
        using var registration = MeshLockRenewalThread.Instance.Register(this);
        var isExpired = await registration.WhenExpired(cancellationToken);
        if (!isExpired)
            return; // normal shutdown

        // Lock expired during renewal
        lock (Lock)
            IsExpiredOnRenewal = true;
        Log?.LogError("[+-] {Key}: reported as expired on renewal", FullKey);
        _ = DisposeAsync();
    }

    protected override async Task OnStop()
    {
        Task[]? dependencies;
        lock (Lock) {
            dependencies = _dependencies?.ToArray() ?? [];
            _dependencies = null;
        }
        try {
            if (dependencies.Length > 0) {
                DebugLog?.LogDebug("[+-] {Key}: stopping {Count} dependent task(s)...", FullKey, dependencies.Length);
                foreach (var dependency in dependencies)
                    await dependency.SilentAwait(false);
            }
        }
        finally {
            var result = IsExpiredOnRenewal
                ? MeshLockReleaseResult.ExpiredOnRenewal
                : await TryRelease().ConfigureAwait(false);
            DebugLog?.LogDebug("[-] {Key}: released -> {Result}", FullKey, result.ToString("G"));
        }
    }

    /// <summary>
    /// Single synchronous renewal attempt — runs entirely on the dedicated renewal thread,
    /// immune to ThreadPool starvation. Returns true if renewed, false if lock expired.
    /// Throws on transient errors (e.g. Redis connection) — the renewal thread
    /// will schedule a retry with backoff.
    /// </summary>
    private bool TryRenewBlocking(CancellationToken cancellationToken)
    {
        var expiresIn = ExpiresAt - Options.ExpirationSafetyMargin - Clock.Now;
        if (expiresIn < TimeSpan.Zero) {
            Log?.LogError("[+*] {Key}: renewal failed - too late to renew", FullKey);
            return false;
        }

        // Limit blocking time to avoid stalling other locks' renewals
        var callTimeout = TimeSpanExt.Min(expiresIn, Options.ExpirationSafetyMargin);
        var expiredCts = cancellationToken.CreateLinkedTokenSource();
        var expiredToken = expiredCts.Token;
        expiredCts.CancelAfter(callTimeout);
        try {
            var expiresAt = Clock.Now + Options.ExpirationPeriod;
                var isRenewed = _backend
                .TryRenewBlocking(Key, Id, Options.ExpirationPeriod, expiredToken);
            if (isRenewed)
                ExpiresAt = expiresAt;
            else
                Log?.LogError("[+*] {Key}: renewal failed - key already expired", FullKey);
            return isRenewed;
        }
        // ReSharper disable once PossiblyMistakenUseOfCancellationToken
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            if (expiredToken.IsCancellationRequested) {
                Log?.LogError(e, "[+*] {Key}: renewal failed - timeout", FullKey);
                return false;
            }
            // Transient error — let it propagate to the renewal thread for retry scheduling
            throw;
        }
        finally {
            expiredCts.CancelAndDisposeSilently();
        }
    }

    private async Task<MeshLockReleaseResult> TryRelease()
    {
        var failureCount = 0;
        while (true) {
            var expiresIn = ExpiresAt - Clock.Now;
            if (expiresIn < TimeSpan.Zero) {
                Log?.LogError("[+-] {Key}: release failed - too late to release", FullKey);
                return MeshLockReleaseResult.ExpiredOnRelease;
            }

            var timeoutCts = new CancellationTokenSource(expiresIn);
            var timeoutToken = timeoutCts.Token;
            try {
                var result = await _backend.TryRelease(Key, Id, timeoutToken).ConfigureAwait(false);
                if (result == MeshLockReleaseResult.Released) {
                    // Uncomment for debugging - too verbose
                    // Log?.LogDebug("[+*] {Key}: released", FullKey);
                }
                else
                    Log?.LogError("[+-] {Key}: release failed - {Result}", FullKey, result);
                return result;
            }
            catch (Exception e) {
                if (timeoutToken.IsCancellationRequested) {
                    Log?.LogError(e, "[+-] {Key}: release failed - timeout", FullKey);
                    return MeshLockReleaseResult.ExpiredOnRelease;
                }

                failureCount++;
                Log?.LogError(e, "[+-] {Key}: release failed, will retry", FullKey);
            }
            finally {
                timeoutCts.CancelAndDisposeSilently();
            }

            // Backoff before retrying - the expiresIn check at the top of the loop
            // will catch the case where we've run past the deadline
            // ReSharper disable once MethodSupportsCancellation
            await Clock.Delay(_backend.RetryDelays[failureCount]).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Process-wide singleton that runs one dedicated OS thread servicing
    /// all <see cref="MeshLockHolder"/> renewals. This thread is immune to
    /// ThreadPool starvation, which can cause lock expiry under heavy async load.
    /// The thread never blocks on Redis calls — it fires them asynchronously
    /// and checks results on the next iteration.
    /// </summary>
    private sealed class MeshLockRenewalThread
    {
        public static readonly MeshLockRenewalThread Instance = new();

        private readonly ConcurrentDictionary<MeshLockHolder, RenewalEntry> _entries = new();
        private readonly SemaphoreSlim _wakeSemaphore = new(0, 1);

        private MeshLockRenewalThread()
        {
            var thread = new Thread(Run) {
                IsBackground = true,
                Name = "MeshLockRenewal",
                Priority = ThreadPriority.AboveNormal,
            };
            thread.Start();
        }

        public ThreadRegistration Register(MeshLockHolder holder)
        {
            var entry = new RenewalEntry {
                NextRenewalAt = holder.Clock.Now + holder.Options.RenewalPeriod,
            };
            _entries[holder] = entry;
            // Wake the thread when the holder is stopped so entries are cleaned up promptly
            var wakeOnStop = holder.StopToken.Register(TryWakeUp);
            TryWakeUp();
            return new ThreadRegistration(this, holder, entry.ExpiredTcs.Task, wakeOnStop);
        }

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

        private void Run()
        {
            while (true) {
                var minSleepMs = 1000;

                foreach (var (holder, entry) in _entries.ToArray()) {
                    try {
                        if (entry.ExpiredTcs.Task.IsCompleted) {
                            _entries.TryRemove(holder, out _);
                            continue;
                        }

                        var ct = holder.StopToken;
                        if (ct.IsCancellationRequested) {
                            _entries.TryRemove(holder, out _);
                            entry.ExpiredTcs.TrySetCanceled(ct);
                            continue;
                        }

                        // Check if it's time to renew (synchronous — runs on this dedicated thread)
                        var now = holder.Clock.Now;
                        var remaining = entry.NextRenewalAt - now;

                        if (remaining <= TimeSpan.Zero) {
                            // Re-check: holder may have been stopped between the earlier check and now
                            if (ct.IsCancellationRequested) {
                                _entries.TryRemove(holder, out _);
                                entry.ExpiredTcs.TrySetCanceled(ct);
                                continue;
                            }
                            try {
                                var isHeld = holder.TryRenewBlocking(ct);
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
                            continue;
                        }

                        var remainingMs = (int)remaining.TotalMilliseconds;
                        if (remainingMs < minSleepMs)
                            minSleepMs = remainingMs;
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
                _ = _wakeSemaphore.Wait(Math.Max(10, minSleepMs));
            }
        }

        public readonly struct ThreadRegistration : IDisposable
        {
            private readonly MeshLockRenewalThread _thread;
            private readonly MeshLockHolder _holder;
            private readonly Task _expiredTask;
            private readonly CancellationTokenRegistration _wakeOnStop;
            internal ThreadRegistration(
                MeshLockRenewalThread thread,
                MeshLockHolder holder,
                Task expiredTask,
                CancellationTokenRegistration wakeOnStop)
            {
                _thread = thread;
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
                var cancelTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                await using var ctr = cancellationToken.Register(
                    static state => ((TaskCompletionSource)state!).TrySetResult(),
                    cancelTcs);
                if (cancellationToken.IsCancellationRequested)
                    return false;

                var completedTask = await Task.WhenAny(_expiredTask, cancelTcs.Task).ConfigureAwait(false);
                return completedTask == _expiredTask && _expiredTask.IsCompletedSuccessfully;
            }

            public void Dispose()
            {
                _wakeOnStop.Dispose();
                _thread.Unregister(_holder);
            }
        }

        private sealed class RenewalEntry
        {
            public readonly TaskCompletionSource ExpiredTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public Moment NextRenewalAt;
            public int FailureCount;
        }
    }
}
