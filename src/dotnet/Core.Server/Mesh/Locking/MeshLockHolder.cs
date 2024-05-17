using Cysharp.Text;

namespace ActualChat.Mesh;

public class MeshLockHolder : WorkerBase, IHasId<string>
{
    protected readonly IMeshLocksBackend Backend;
    protected IMomentClock Clock => Backend.Clock;
    protected ILogger? Log => Backend.Log;
    protected ILogger? DebugLog => Backend.DebugLog;
    protected HashSet<Task>? Dependencies;

    public string Id { get; } // This is the ID of the lock holder, i.e. this object
    public string Key { get; }
    public string FullKey { get; }
    public string Value { get; }
    public string StoredValue { get; }
    public MeshLockOptions Options { get; }
    public TimeSpan MinExpiresIn { get; init; } = TimeSpan.FromSeconds(0.25);
    public CpuTimestamp CreatedAt { get; }
    public CpuTimestamp ExpiresAt { get; protected set; }

    public MeshLockHolder(
        IMeshLocksBackend backend,
        string id,
        string key,
        string value,
        MeshLockOptions options)
    {
        if (key.IsNullOrEmpty())
            throw new ArgumentOutOfRangeException(nameof(key));
        options.AssertValid();

        Backend = backend;
        Id = id;
        Key = key;
        FullKey = backend.GetFullKey(key);
        Value = value;
        StoredValue = ZString.Concat(id, ' ', value);
        Options = options;
        ExpiresAt = CreatedAt = CpuTimestamp.Now;
    }

    public Task AddDependency(Func<CancellationToken, Task> dependencyFactory, bool autoRemove = true)
    {
        Task dependency;
        lock (Lock) {
            StopToken.ThrowIfCancellationRequested();
            var dependencies = Dependencies ??= new();
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

    // ParseXxx

    public static (string Value, string HolderId) ParseStoredValue(string storedValue)
    {
        var spaceIndex = storedValue.OrdinalIndexOf(' ');
        if (spaceIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(storedValue));

        var holderId = storedValue[..spaceIndex];
        var value = storedValue[(spaceIndex + 1)..];
        return (value, holderId);
    }

    // Protected methods

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        DebugLog?.LogDebug("[+] {Key}: acquired in {AcquireTime}, value = {StoredValue}",
            FullKey, CreatedAt.Elapsed.ToShortString(), StoredValue);
        var expirationPeriod = Options.ExpirationPeriod;
        var renewPeriod = Options.RenewalPeriod;
        while (true) {
            ExpiresAt = CpuTimestamp.Now + expirationPeriod;
            var now = Clock.Now;
            var expiresAt = now + expirationPeriod;
            var renewsAt = now + renewPeriod;
            var delay = renewsAt - now;
            if (delay > TimeSpan.Zero)
                await Clock.Delay(delay, cancellationToken).ConfigureAwait(false);
            else if (now > expiresAt) {
                Log?.LogError("[+-] {Key}: must be expired based on its last renewal time", FullKey);
                break;
            }

            var isRenewed = await TryRenew(expiresAt, cancellationToken).ConfigureAwait(false);
            if (!isRenewed) {
                Log?.LogError("[+-] {Key}: reported as expired on renewal", FullKey);
                break;
            }
        }
        _ = DisposeAsync();
    }

    protected override async Task OnStop()
    {
        Task[]? dependencies;
        lock (Lock) {
            dependencies = Dependencies?.ToArray() ?? Array.Empty<Task>();
            Dependencies = null;
        }
        try {
            if (dependencies.Length > 0) {
                DebugLog?.LogDebug("[+-] {Key}: stopping {Count} dependent task(s)...", FullKey, dependencies.Length);
                foreach (var dependency in dependencies)
                    await dependency.SilentAwait();
            }
        }
        finally {
            var result = await TryRelease().ConfigureAwait(false);
            DebugLog?.LogDebug("[-] {Key}: released -> {Result}", FullKey, result.ToString("G"));
        }
    }

    protected async Task<bool> TryRenew(Moment expiresAt, CancellationToken cancellationToken)
    {
        while (true) {
            var expiresIn = expiresAt - Clock.Now;
            if (expiresIn < MinExpiresIn) {
                Log?.LogError("[+*] {Key}: renewal failed - too late to renew", FullKey);
                return false;
            }

            var cts = cancellationToken.CreateLinkedTokenSource();
            cts.CancelAfter(expiresIn);
            try {
                var isRenewed = await Backend
                    .TryRenew(Key, StoredValue, Options.ExpirationPeriod, cts.Token)
                    .ConfigureAwait(false);
                // Uncomment for debugging - too verbose
                // Log?.LogDebug("[+*] {Key}: renewed", FullKey);
                return isRenewed;
            }
            catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
                if (cts.Token.IsCancellationRequested) {
                    Log?.LogError(e, "[+*] {Key}: renewal failed", FullKey);
                    return false;
                }

                Log?.LogError(e, "[+*] {Key}: renewal failed, will retry", FullKey);
            }
            finally {
                cts.CancelAndDisposeSilently();
            }
        }
    }

    protected async Task<MeshLockReleaseResult> TryRelease()
    {
        while (true) {
            var expiresIn = -ExpiresAt.Elapsed;
            if (expiresIn < MinExpiresIn) {
                Log?.LogError("[+-] {Key}: release failed - too late to release", FullKey);
                return MeshLockReleaseResult.Expired;
            }

            var cts = new CancellationTokenSource(expiresIn);
            try {
                return await Backend
                    .TryRelease(Key, StoredValue, cts.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception e) {
                if (cts.Token.IsCancellationRequested) {
                    Log?.LogError(e, "[+-] {Key}: release failed", FullKey);
                    return MeshLockReleaseResult.Expired;
                }

                Log?.LogError(e, "[+-] {Key}: release failed, will retry", FullKey);
            }
            finally {
                cts.CancelAndDisposeSilently();
            }
        }
    }
}
