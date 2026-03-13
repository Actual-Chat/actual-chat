using System.Net;
using ActualChat.Hashing;
using ActualChat.Kubernetes.Api;

namespace ActualChat.Kubernetes;

public class KubeMeshLocks : MeshLocksBase
{
    public const string KeyPrefix = "voxt.ai/key-prefix";
    public const string FullName = "voxt.ai/full-name";
    public static readonly TimeSpan StaleLeaseAge = TimeSpan.FromDays(7);

    private readonly ConcurrentDictionary<string, (string FullName, string LeaseName)> _leaseFullKeys = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Api.Lease> _cachedLeases = new(StringComparer.Ordinal);
    private readonly string _keyPrefix;
    private readonly int _keyPrefixLength;
    private readonly string _labelSelector;
    private KubeLeaseClient LeaseClient { get; }
    private string Namespace { get; }

    public KubeMeshLocks(IServiceProvider services, string keyPrefix = "", string @namespace = "")
        : base(services)
    {
        _keyPrefix = keyPrefix.IsNullOrEmpty() ? DefaultKeyPrefix : keyPrefix;
        _keyPrefixLength = _keyPrefix.Length + 1; // including dash "-"
        LeaseClient = services.GetRequiredService<KubeLeaseClient>();
        // We use the namespace where the pod is running
        Namespace = @namespace.IsNullOrEmpty()
            ? Environment.GetEnvironmentVariable("POD_NAMESPACE").NullIfEmpty() ?? "default"
            : @namespace;
        _labelSelector = $"{KeyPrefix}={_keyPrefix}";
    }

    public override string GetFullKey(string key)
    {
        var (fullName, _) = GetName(key);
        return fullName;
    }

    public override async Task<MeshLockInfo?> GetInfo(string key, CancellationToken cancellationToken = default)
    {
        var (_, name) = GetName(key);
        var lease = await LeaseClient.Get(Namespace, name, cancellationToken).ConfigureAwait(false);
        if (lease?.Metadata.Annotations == null)
            return null;

        return lease.Spec.HolderIdentity == null
            ? null
            : new MeshLockInfo(lease.Metadata.Annotations[FullName], lease.Spec.HolderIdentity);
    }

    public override Task<IAsyncSubscription<string>> Changes(string key, CancellationToken cancellationToken = default)
    {
        var (_, name) = GetName(key);
        var subscription = new KubeSubscription<string>(Clock);
        _ = Task.Run(async () => {
            try {
                while (!cancellationToken.IsCancellationRequested)
                    // Watch leases with the specific label selector for some period of time, e.g. 30s
                    await LeaseClient.Watch(Namespace, _labelSelector, OnChange, RetryDelays, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
                Log.LogWarning(e, "Failed to watch leases");
                await subscription.DisposeSilentlyAsync().ConfigureAwait(false);
            }
        }, cancellationToken);
        return Task.FromResult<IAsyncSubscription<string>>(subscription);

        async Task OnChange(Api.Change<Api.Lease> change, CancellationToken ct) {
            var lease = change.Object;
            if (lease?.Metadata?.Name == null)
                return;

            if (IsExpired(lease) && change.Type == ChangeType.Added)
                return; // Avoid processing an initial result of lease watch for expired leases

            if (lease.Metadata.Name == name || key.IsNullOrEmpty()) {
                var fullName = lease.Metadata.Annotations?[FullName];
                if (fullName == null && _leaseFullKeys.TryGetValue(key, out var names))
                    fullName = names.FullName;
                if (fullName == null) {
                    Log.LogWarning("Lease {LeaseName} has no full name annotation", lease.Metadata.Name);
                    return;
                }
                await subscription.Push(fullName[_keyPrefixLength..], ct)
                    .ConfigureAwait(false);
            }
        }
    }

    public override async Task<List<string>> ListKeys(string prefix, CancellationToken cancellationToken = default)
    {
        var leases = await LeaseClient.List(Namespace, _labelSelector, cancellationToken).ConfigureAwait(false);
        var (fullPrefix, _) = GetName(prefix);
        var activeKeys = new List<string>();
        var staleLeases = new List<Lease>();
        var staleThreshold = (Clock.Now - StaleLeaseAge).ToDateTime();
        foreach (var lease in leases.Items) {
            var isExpired = IsExpired(lease);
            var hasAnnotation = lease.Metadata.Annotations != null
                && lease.Metadata.Annotations.TryGetValue(FullName, out var fullName)
                && fullName.StartsWith(fullPrefix, StringComparison.Ordinal);

            if (!isExpired && hasAnnotation)
                activeKeys.Add(lease.Metadata.Annotations![FullName][_keyPrefixLength..]);
            else if (isExpired && (lease.Spec.RenewTime ?? DateTime.MinValue) < staleThreshold)
                staleLeases.Add(lease);
        }

        if (staleLeases.Count > 0)
            _ = Task.Run(() => PruneStale(staleLeases), CancellationToken.None);

        return activeKeys;
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
        var requestTimeout = KubeLeaseClient.DefaultRequestTimeout;
        var (fullName, name) = GetName(key);
        var now = Clock.Now.ToDateTime();
        var lease = new Lease(
            new Metadata(name, Namespace) {
                Labels = new Labels {
                    { KeyPrefix, _keyPrefix },
                },
                Annotations = new Annotations {
                    { FullName, fullName}
                }
            },
            new LeaseSpec(value, (int)expiresIn.TotalSeconds, now, now)
        );

        try {
            var created = await LeaseClient.Create(Namespace, lease, cancellationToken, requestTimeout).ConfigureAwait(false);
            _cachedLeases[key] = created;
            return true;
        }
        catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.Conflict) {
            // Lease already exists, check if it's expired
            var existingLease = await LeaseClient.Get(Namespace, name, cancellationToken, requestTimeout).ConfigureAwait(false);
            if (existingLease == null)
                return await TryLock(key, value, expiresIn, cancellationToken).ConfigureAwait(false);

            if (!IsExpired(existingLease))
                return false;

            // Try to take over the expired lease
            existingLease = existingLease with {
                Spec = existingLease.Spec with {
                    HolderIdentity = value,
                    LeaseDurationSeconds = (int)expiresIn.TotalSeconds,
                    RenewTime = now,
                },
                Metadata = existingLease.Metadata with {
                    Labels = lease.Metadata.Labels,
                    Annotations = lease.Metadata.Annotations,
                }
            };
            try {
                var replaced = await LeaseClient.Replace(Namespace, existingLease, cancellationToken, requestTimeout).ConfigureAwait(false);
                _cachedLeases[key] = replaced;
                return true;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Conflict) {
                return false;
            }
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to create a K8s lease '{LeaseName}'", lease.Metadata.Name);
            return false;
        }
    }

    protected override async Task<bool> TryRenew(string key, string value, TimeSpan expiresIn, CancellationToken cancellationToken)
    {
        var requestTimeout = KubeLeaseClient.DefaultRequestTimeout;
        var (_, name) = GetName(key);
        var now = Clock.Now.ToDateTime();

        // Try using cached lease first (single Replace call instead of GET+Replace)
        if (_cachedLeases.TryGetValue(key, out var cachedLease)
            && cachedLease.Spec.HolderIdentity == value
            && !IsExpired(cachedLease)) {
            var updatedLease = cachedLease with {
                Spec = cachedLease.Spec with {
                    LeaseDurationSeconds = (int)expiresIn.TotalSeconds,
                    RenewTime = now,
                }
            };
            try {
                var replaced = await LeaseClient.Replace(Namespace, updatedLease, cancellationToken, requestTimeout).ConfigureAwait(false);
                _cachedLeases[key] = replaced;
                return true;
            }
            catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.Conflict) {
                // Cached resourceVersion is stale - fall through to GET+Replace
                _cachedLeases.TryRemove(key, out _);
            }
        }

        // Fallback: GET + Replace
        var existingLease = await LeaseClient.Get(Namespace, name, cancellationToken, requestTimeout).ConfigureAwait(false);
        if (existingLease == null || existingLease.Spec.HolderIdentity != value || IsExpired(existingLease))
            return false;

        existingLease = existingLease with {
            Spec = existingLease.Spec with {
                LeaseDurationSeconds = (int)expiresIn.TotalSeconds,
                RenewTime = now,
            }
        };

        try {
            var replaced = await LeaseClient.Replace(Namespace, existingLease, cancellationToken, requestTimeout).ConfigureAwait(false);
            _cachedLeases[key] = replaced;
            return true;
        }
        catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.Conflict) {
            _cachedLeases.TryRemove(key, out _);
            return false;
        }
    }

    protected override async Task<MeshLockReleaseResult> TryRelease(string key, string value, CancellationToken cancellationToken)
    {
        var requestTimeout = KubeLeaseClient.DefaultRequestTimeout;
        var (_, name) = GetName(key);
        var existingLease = await LeaseClient.Get(Namespace, name, cancellationToken, requestTimeout).ConfigureAwait(false);
        if (existingLease == null) {
            _cachedLeases.TryRemove(key, out _);
            return MeshLockReleaseResult.Released;
        }

        if (existingLease.Spec.HolderIdentity != value)
            return MeshLockReleaseResult.AcquiredBySomeoneElse;

        try {
            await LeaseClient.Delete(Namespace, name, cancellationToken, requestTimeout).ConfigureAwait(false);
            _cachedLeases.TryRemove(key, out _);
            return MeshLockReleaseResult.Released;
        }
        catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.Conflict) {
            // This might happen if someone else modified it in between, but if we are the holder it shouldn't really happen with Delete unless there's a race
            return MeshLockReleaseResult.UnknownError;
        }
    }

    protected override async Task<bool> ForceRelease(string key, bool mustNotify, CancellationToken cancellationToken)
    {
        var requestTimeout = KubeLeaseClient.DefaultRequestTimeout;
        var (_, name) = GetName(key);
        var result = await LeaseClient.Delete(Namespace, name, cancellationToken, requestTimeout).ConfigureAwait(false);
        _cachedLeases.TryRemove(key, out _);
        return result;
    }

    private (string FullName, string LeaseName) GetName(string key)
        => _leaseFullKeys.GetOrAdd(key,
            static (k, self) => {
                var fullName = self._keyPrefix + "-"+ k;
                if (fullName.Length < 31)
                    return (fullName, fullName.Replace(" ", "-").Replace(",", ".").Replace(":", "").ToKebabCase());

                var hashSuffix = fullName.Hash().Blake3().Base32(16);
                var splitIndex = fullName.IndexOfAny([',', ':', ' ', '.']);
                if (splitIndex < 0)
                    splitIndex = 31;

                var name = fullName[..splitIndex].Replace(" ", "-").Replace(",", ".").Replace(":", "").ToKebabCase() + "-" + hashSuffix;
                return (fullName, name);
            }, this);

    private bool IsExpired(Lease lease)
    {
        if (lease.Spec.RenewTime == null || lease.Spec.LeaseDurationSeconds == null)
            return true;

        var expireTime = lease.Spec.RenewTime.Value.AddSeconds(lease.Spec.LeaseDurationSeconds.Value);
        return expireTime < Clock.Now.ToDateTime();
    }

    private async Task PruneStale(IEnumerable<Lease> leases)
    {
        var now = Clock.Now;
        foreach (var lease in leases) {
            var age = (now - new Moment(lease.Spec.RenewTime ?? DateTime.MinValue)).Positive();
            var sAge = age < TimeSpan.FromDays(30)
                ? age.ToShortString()
                : "more than 30 days";
            try {
                await LeaseClient.Delete(Namespace, lease.Metadata.Name, CancellationToken.None).ConfigureAwait(false);
                Log.LogInformation("Pruned lease: {LeaseName} (age: {Age})", lease.Metadata.Name, sAge);
            }
            catch (Exception e) {
                Log.LogWarning(e, "Failed to prune lease: {LeaseName} (age: {Age})", lease.Metadata.Name, sAge);
            }
        }
    }
}

internal sealed class KubeSubscription<T>(MomentClock clock) : IAsyncSubscription<T>
{
    private readonly Channel<T> _channel = Channel.CreateUnbounded<T>();

    public ChannelReader<T> Reader => _channel.Reader;
    public MomentClock Clock { get; } = clock;

    public async Task Push(T item, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        await _channel.Writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
