namespace ActualChat.Mesh;

/// <summary>
/// Maintains a distributed lock with automatic renewal and dependency tracking.
/// </summary>
public class MeshLockHolder : WorkerBase, IHasId<string>
{
    internal readonly IMeshLocksBackend _backend;
    private HashSet<Task>? _dependencies;
    private long _expiresAtTicks;

    internal MomentClock Clock => _backend.Clock;
    internal ILogger? Log => _backend.Log;
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

        // Register with shared renewal threads — dedicated OS threads for all locks
        using var registration = _backend.RenewalThreads.Register(this);
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
    /// Single synchronous renewal attempt — runs entirely on a dedicated renewal thread,
    /// immune to ThreadPool starvation. Returns true if renewed, false if lock expired.
    /// Throws on transient errors (e.g. Redis connection) — the renewal thread
    /// will schedule a retry with backoff.
    /// </summary>
    internal bool TryRenewBlocking(CancellationToken cancellationToken)
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

}
