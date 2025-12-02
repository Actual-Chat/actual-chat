namespace ActualChat.Mesh;

public class MeshLockHolder : WorkerBase, IHasId<string>
{
    protected readonly IMeshLocksBackend Backend;
    protected MomentClock Clock => Backend.Clock;
    protected ILogger Log => Backend.Log;
    protected ILogger? DebugLog => Backend.DebugLog;
    protected HashSet<Task>? Dependencies;

    public string Id { get; } // This is the ID of the lock holder, i.e., this object
    public string Key { get; }
    public string FullKey { get; }
    public MeshLockOptions Options { get; }
    public Moment CreatedAt { get; }
    public Moment ExpiresAt { get; protected set; }
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

        Backend = backend;
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
            var dependencies = Dependencies ??= new ();
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
            Dependencies?.Remove(dependency);
    }

    // Protected methods

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var now = Clock.Now;
        ExpiresAt = now + Options.ExpirationPeriod;
        DebugLog?.LogDebug(
            "[+] {Key}: acquired in {AcquireTime}, value = {StoredValue}",
            FullKey, (now - CreatedAt).ToShortString(), Id);

        var chaosMaker = Backend.ChaosMaker;
        if (chaosMaker.IsEnabled)
            _ = Task.Run(async () => {
                await chaosMaker.Act(this, cancellationToken).SilentAwait();
                if (cancellationToken.IsCancellationRequested)
                    return;

                Log.LogWarning("[!] {Key}: ChaosMaker-caused termination", FullKey);
                _ = Stop();
            }, CancellationToken.None);

        var isHeld = true;
        while (isHeld) {
            await Clock.Delay(Options.RenewalPeriod, cancellationToken).ConfigureAwait(false);
            isHeld = await TryRenew(cancellationToken).ConfigureAwait(false);
        }
        lock (Lock)
            IsExpiredOnRenewal = true;
        Log.LogError("[+-] {Key}: reported as expired on renewal", FullKey);
        _ = DisposeAsync();
    }

    protected override async Task OnStop()
    {
        Task[]? dependencies;
        lock (Lock) {
            dependencies = Dependencies?.ToArray() ?? [];
            Dependencies = null;
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

    protected async Task<bool> TryRenew(CancellationToken cancellationToken)
    {
        while (true) {
            var expiresIn = ExpiresAt - Options.ExpirationSafetyMargin - Clock.Now;
            if (expiresIn < TimeSpan.Zero) {
                Log.LogError("[+*] {Key}: renewal failed - too late to renew", FullKey);
                return false;
            }

            var expiredCts = cancellationToken.CreateLinkedTokenSource();
            var expiredToken = expiredCts.Token;
            expiredCts.CancelAfter(expiresIn);
            try {
                var expiresAt = Clock.Now + Options.ExpirationPeriod;
                // DebugLog?.LogDebug("[+*] {Key}: renew {StoredValue}", FullKey, StoredValue);
                var isRenewed = await Backend
                    .TryRenew(Key, Id, Options.ExpirationPeriod, expiredToken)
                    .ConfigureAwait(false);
                if (isRenewed) {
                    ExpiresAt = expiresAt;
                    // Uncomment for debugging - too verbose
                    // Log?.LogDebug("[+*] {Key}: renewed", FullKey);
                }
                else
                    Log.LogError("[+*] {Key}: renewal failed - key already expired", FullKey);
                return isRenewed;
            }
            // ReSharper disable once PossiblyMistakenUseOfCancellationToken
            catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
                if (expiredToken.IsCancellationRequested) {
                    Log.LogError(e, "[+*] {Key}: renewal failed - timeout", FullKey);
                    return false;
                }

                Log.LogError(e, "[+*] {Key}: renewal failed, will retry", FullKey);
            }
            finally {
                expiredCts.CancelAndDisposeSilently();
            }
        }
    }

    protected async Task<MeshLockReleaseResult> TryRelease()
    {
        while (true) {
            var expiresIn = ExpiresAt - Clock.Now;
            if (expiresIn < TimeSpan.Zero) {
                Log.LogError("[+-] {Key}: release failed - too late to release", FullKey);
                return MeshLockReleaseResult.ExpiredOnRelease;
            }

            var timeoutCts = new CancellationTokenSource(expiresIn);
            var timeoutToken = timeoutCts.Token;
            try {
                var result = await Backend.TryRelease(Key, Id, timeoutToken).ConfigureAwait(false);
                if (result == MeshLockReleaseResult.Released) {
                    // Uncomment for debugging - too verbose
                    // Log?.LogDebug("[+*] {Key}: released", FullKey);
                }
                else
                    Log.LogError("[+-] {Key}: release failed - {Result}", FullKey, result);
                return result;
            }
            catch (Exception e) {
                if (timeoutToken.IsCancellationRequested) {
                    Log.LogError(e, "[+-] {Key}: release failed - timeout", FullKey);
                    return MeshLockReleaseResult.ExpiredOnRelease;
                }

                Log.LogError(e, "[+-] {Key}: release failed, will retry", FullKey);
            }
            finally {
                timeoutCts.CancelAndDisposeSilently();
            }
        }
    }
}
