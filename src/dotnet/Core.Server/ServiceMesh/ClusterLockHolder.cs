using Cysharp.Text;

namespace ActualChat.ServiceMesh;

public class ClusterLockHolder(
    ClusterLocksBackend backend,
    Symbol key,
    string value,
    string holderId,
    ClusterLockOptions options
    ) : WorkerBase
{
    protected readonly ClusterLocksBackend Backend = backend;
    protected IMomentClock Clock => Backend.Clock;
    protected HashSet<Task>? Dependencies;

    public Symbol Key { get; } = key;
    public string Value { get; } = value;
    public string HolderId { get; } = holderId;
    public string StoredValue { get; } = ZString.Concat(holderId, ' ', value);
    public ClusterLockOptions Options { get; } = options;

    public static (string Value, string HolderId) ParseStoredValue(string storedValue)
    {
        var spaceIndex = storedValue.OrdinalIndexOf(' ');
        if (spaceIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(storedValue));

        var holderId = storedValue[..spaceIndex];
        var value = storedValue[(spaceIndex + 1)..];
        return (value, holderId);
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

    // Protected methods

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var keepLockedTask = KeepLocked(cancellationToken);
        var whenReleasedTask = WhenReleased(cancellationToken);
        await Task.WhenAll(keepLockedTask, whenReleasedTask).SilentAwait();
    }

    protected override async Task OnStop()
    {
        Task[]? dependencies;
        lock (Lock) {
            dependencies = Dependencies?.ToArray() ?? Array.Empty<Task>();
            Dependencies = null;
        }
        try {
            if (dependencies.Length > 0)
                await Task.WhenAll(dependencies).SilentAwait();
        }
        finally {
            await Backend.TryRelease(Key, StoredValue, CancellationToken.None).ConfigureAwait(false);
        }
    }

    // Private methods

    private async Task KeepLocked(CancellationToken cancellationToken)
    {
        var renewPeriod = Options.ExpirationPeriod * Options.RenewalPeriod;
        while (true) {
            await Clock.Delay(renewPeriod, cancellationToken).ConfigureAwait(false);
            await Backend.TryRenew(Key, StoredValue, Options.ExpirationPeriod, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task WhenReleased(CancellationToken cancellationToken)
    {
        while (true) {
            await Clock.Delay(Options.CheckPeriod, cancellationToken).ConfigureAwait(false);
            var lockInfo = await Backend.TryQuery(Key, cancellationToken).ConfigureAwait(false);
            if (lockInfo == null || !OrdinalEquals(lockInfo.HolderId, HolderId))
                return;
        }
    }
}
