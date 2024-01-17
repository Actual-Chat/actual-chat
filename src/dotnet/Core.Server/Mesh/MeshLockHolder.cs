using Cysharp.Text;

namespace ActualChat.Mesh;

public class MeshLockHolder : WorkerBase, IHasId<string>
{
    protected readonly IMeshLocksBackend Backend;
    protected IMomentClock Clock => Backend.Clock;
    protected ILogger? Log => Backend.Log;
    protected HashSet<Task>? Dependencies;

    public string Id { get; } // This is the ID of the lock holder, i.e. this object
    public Symbol Key { get; }
    public string Value { get; }
    public string StoredValue { get; }
    public MeshLockOptions Options { get; }
    public RandomTimeSpan RetryPeriod { get; init; } = TimeSpan.FromSeconds(0.5).ToRandom(0.1);
    public TimeSpan MaxClockDrift { get; init; } = TimeSpan.FromMicroseconds(100);
    public CpuTimestamp CreatedAt { get; } = CpuTimestamp.Now;

    public MeshLockHolder(IMeshLocksBackend backend, string id, Symbol key, string value, MeshLockOptions options)
    {
        options.AssertValid();
        Backend = backend;
        Id = id;
        Key = key;
        Value = value;
        StoredValue = ZString.Concat(id, ' ', value);
        Options = options;
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
        Log?.LogInformation("[+] {Key} = {StoredValue} (acquired in {AcquireTime})",
            Key, StoredValue, CreatedAt.Elapsed.ToShortString());
        var expirationPeriod = Options.ExpirationPeriod;
        var renewPeriod = Options.RenewalPeriod;
        var now = Clock.Now;
        var renewsAt = now + renewPeriod;
        var expiresAt = now + expirationPeriod + MaxClockDrift;
        while (true) {
            now = Clock.Now;
            var delay = renewsAt - now;
            if (delay > TimeSpan.Zero)
                await Clock.Delay(delay, cancellationToken).ConfigureAwait(false);
            else if (now > expiresAt) {
                Log?.LogError("[+-] {Key}: must be expired based on its last renewal time", Key);
                break;
            }

            try {
                var isRenewed = await Backend
                    .TryRenew(Key, StoredValue, expirationPeriod, cancellationToken)
                    .ConfigureAwait(false);
                if (!isRenewed) {
                    Log?.LogError("[+-] {Key}: reported as expired on renewal", Key);
                    break;
                }

                now = Clock.Now;
                renewsAt = now + renewPeriod;
                expiresAt = now + expirationPeriod + MaxClockDrift;
            }
            catch (Exception e) when (!cancellationToken.IsCancellationRequested) {
                var retryPeriod = RetryPeriod.Next();
                Log?.LogError(e, "[+*] {Key}: renewal error, will retry in {RetryPeriod}", Key, retryPeriod.ToShortString());
                await Clock.Delay(retryPeriod, CancellationToken.None).ConfigureAwait(false);
            }
        }
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
                Log?.LogInformation("[+-] {Key}: stopping {Count} dependent task(s)...", Key, dependencies.Length);
                foreach (var dependency in dependencies)
                    await dependency.SilentAwait();
            }
        }
        finally {
            Log?.LogInformation("[+-] {Key}: releasing...", Key);
            var expirationPeriod = Options.ExpirationPeriod;
            var expiresAt = Clock.Now + expirationPeriod + MaxClockDrift;
            var result = MeshLockReleaseResult.Unknown;
            while (Clock.Now <= expiresAt) {
                try {
                    result = await Backend.TryRelease(Key, StoredValue, CancellationToken.None).ConfigureAwait(false);
                    break;
                }
                catch (Exception e) {
                    var retryPeriod = RetryPeriod.Next();
                    Log?.LogError(e, "[+-] {Key}: release error, will retry in {RetryPeriod}", Key, retryPeriod.ToShortString());
                    await Clock.Delay(retryPeriod, CancellationToken.None).ConfigureAwait(false);
                }
            }
            Log?.LogInformation("[-] {Key} = {StoredValue} -> {Result}", Key, StoredValue, result.ToString("G"));
        }
    }
}
