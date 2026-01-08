using System.Net;

namespace ActualChat.Kubernetes;

public class KubeMeshLocks : MeshLocksBase
{
    private readonly string _keyPrefix;
    private KubeLeaseClient LeaseClient { get; }
    private string Namespace { get; }

    public KubeMeshLocks(IServiceProvider services, string keyPrefix = "", string @namespace = "")
        : base(services)
    {
        _keyPrefix = keyPrefix.IsNullOrEmpty() ? DefaultKeyPrefix : keyPrefix;
        LeaseClient = services.GetRequiredService<KubeLeaseClient>();
        // We use the namespace where the pod is running
        Namespace = @namespace.IsNullOrEmpty()
            ? Environment.GetEnvironmentVariable("POD_NAMESPACE").NullIfEmpty() ?? "default"
            : @namespace;
    }

    public override string GetFullKey(string key)
        => _keyPrefix + key;

    public override async Task<MeshLockInfo?> GetInfo(string key, CancellationToken cancellationToken = default)
    {
        var lease = await LeaseClient.Get(Namespace, GetFullKey(key), cancellationToken).ConfigureAwait(false);
        return lease?.Spec.HolderIdentity == null ? null : new MeshLockInfo(key, lease.Spec.HolderIdentity);
    }

    public override async Task<IAsyncSubscription<string>> Changes(string key, CancellationToken cancellationToken = default)
    {
        var fullKey = GetFullKey(key);
        var subscription = new KubeSubscription<string>(Clock);
        _ = Task.Run(async () => {
            try {
                await LeaseClient.Watch(Namespace, null, OnChange, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
                // Ignore
            }
        }, cancellationToken);
        return subscription;

        async Task OnChange(Api.Change<Api.Lease> change, CancellationToken ct) {
            var lease = change.Object;
            if (lease.Metadata.Name == fullKey || key.IsNullOrEmpty())
                await subscription.Push(lease.Metadata.Name[_keyPrefix.Length..], ct).ConfigureAwait(false);
        }
    }

    public override async Task<List<string>> ListKeys(string prefix, CancellationToken cancellationToken = default)
    {
        var leases = await LeaseClient.List(Namespace, null, cancellationToken).ConfigureAwait(false);
        var fullPrefix = GetFullKey(prefix);
        return leases.Items
            .Where(x => x.Metadata.Name.StartsWith(fullPrefix, StringComparison.Ordinal))
            .Select(x => x.Metadata.Name[_keyPrefix.Length..])
            .ToList();
    }

    public override IMeshLocks With(string keyPrefix, MeshLockOptions? lockOptions)
    {
        if (keyPrefix.IsNullOrEmpty() && ReferenceEquals(lockOptions, null))
            return this;

        return new KubeMeshLocks(Services, keyPrefix ?? "", Namespace) {
            LockOptions = lockOptions ?? LockOptions,
        };
    }

    protected override async Task<bool> TryLock(string key, string value, TimeSpan expiresIn, CancellationToken cancellationToken)
    {
        var fullKey = GetFullKey(key);
        var now = Clock.Now.ToDateTime();
        var lease = new Api.Lease(
            new Api.Metadata(fullKey, Namespace),
            new Api.LeaseSpec(value, (int)expiresIn.TotalSeconds, now, now)
        );

        try {
            await LeaseClient.Create(Namespace, lease, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.Conflict) {
            // Lease already exists, check if it's expired
            var existingLease = await LeaseClient.Get(Namespace, fullKey, cancellationToken).ConfigureAwait(false);
            if (existingLease == null)
                return await TryLock(key, value, expiresIn, cancellationToken).ConfigureAwait(false);

            if (IsExpired(existingLease)) {
                // Try to take over the expired lease
                existingLease = existingLease with {
                    Spec = existingLease.Spec with {
                        HolderIdentity = value,
                        LeaseDurationSeconds = (int)expiresIn.TotalSeconds,
                        RenewTime = now,
                    },
                };
                try {
                    await LeaseClient.Replace(Namespace, existingLease, cancellationToken).ConfigureAwait(false);
                    return true;
                }
                catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Conflict) {
                    return false;
                }
            }
            return false;
        }
    }

    protected override async Task<bool> TryRenew(string key, string value, TimeSpan expiresIn, CancellationToken cancellationToken)
    {
        var fullKey = GetFullKey(key);
        var existingLease = await LeaseClient.Get(Namespace, fullKey, cancellationToken).ConfigureAwait(false);
        if (existingLease == null || existingLease.Spec.HolderIdentity != value)
            return false;

        var now = Clock.Now.ToDateTime();
        existingLease = existingLease with {
            Spec = existingLease.Spec with {
                LeaseDurationSeconds = (int)expiresIn.TotalSeconds,
                RenewTime = now,
            }
        };

        try {
            await LeaseClient.Replace(Namespace, existingLease, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.Conflict) {
            return false;
        }
    }

    protected override async Task<MeshLockReleaseResult> TryRelease(string key, string value, CancellationToken cancellationToken)
    {
        var fullKey = GetFullKey(key);
        var existingLease = await LeaseClient.Get(Namespace, fullKey, cancellationToken).ConfigureAwait(false);
        if (existingLease == null)
            return MeshLockReleaseResult.Released;

        if (existingLease.Spec.HolderIdentity != value)
            return MeshLockReleaseResult.AcquiredBySomeoneElse;

        try {
            await LeaseClient.Delete(Namespace, fullKey, cancellationToken).ConfigureAwait(false);
            return MeshLockReleaseResult.Released;
        }
        catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.Conflict) {
            // This might happen if someone else modified it in between, but if we are the holder it shouldn't really happen with Delete unless there's a race
            return MeshLockReleaseResult.UnknownError;
        }
    }

    protected override async Task<bool> ForceRelease(string key, bool mustNotify, CancellationToken cancellationToken)
    {
        var fullKey = GetFullKey(key);
        return await LeaseClient.Delete(Namespace, fullKey, cancellationToken).ConfigureAwait(false);
    }

    private bool IsExpired(Api.Lease lease)
    {
        if (lease.Spec.RenewTime == null || lease.Spec.LeaseDurationSeconds == null)
            return true;

        var expireTime = lease.Spec.RenewTime.Value.AddSeconds(lease.Spec.LeaseDurationSeconds.Value);
        return expireTime < Clock.Now.ToDateTime();
    }
}

internal sealed class KubeSubscription<T>(MomentClock clock) : IAsyncSubscription<T>
{
    private readonly Channel<T> _channel = Channel.CreateUnbounded<T>();

    public ChannelReader<T> Reader => _channel.Reader;
    public MomentClock Clock { get; } = clock;

    public async Task Push(T item, CancellationToken cancellationToken)
        => await _channel.Writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);

    public ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
