using ActualChat.Logging;
using ActualLab.Diagnostics;
using ActualLab.Resilience;
using Microsoft.Extensions.Hosting;

namespace ActualChat.Mesh;

public abstract class MeshLocksBase : IMeshLocksBackend
{
    private static bool DebugMode => Constants.DebugMode.MeshLocks;
    public static readonly string DefaultKeyPrefix = "MeshLocks";

    private readonly LazySlim<ILogger?>? _debugLog;

    protected readonly string HolderKeyPrefix = Alphabet.AlphaNumeric.Generator8.Next() + "-";
    protected long LastHolderId;

    protected ILogger? Log => (field ??= Services.LogFor(GetType())).UnlessStopping(HostLifetime);
    protected ILogger? DebugLog => _debugLog?.Value;
    protected ChaosMaker ChaosMaker => field ??= Services.GetRequiredService<ChaosMaker>();
    protected IHostApplicationLifetime? HostLifetime => field ??= Services.GetService<IHostApplicationLifetime>();

    public MeshLockOptions LockOptions { get; init; } = MeshLockOptions.Default;
    public RetryDelaySeq RetryDelays => field ??= RetryDelaySeq.Exp(0.1, LockOptions.ExpirationPeriod.TotalSeconds / 2);

    public IServiceProvider Services { get; init; }
    public MomentClock Clock => field ??= Services.Clocks().SystemClock;
    public IMeshLocksBackend Backend => this;

    // IMeshLocksBackend
    ILogger? IMeshLocksBackend.Log => Log;
    ILogger? IMeshLocksBackend.DebugLog => DebugLog;
    ChaosMaker IMeshLocksBackend.ChaosMaker => ChaosMaker;
    IHostApplicationLifetime? IMeshLocksBackend.HostLifetime => HostLifetime;
    public MeshLockRenewalThreads RenewalThreads => field ??= Services.GetRequiredService<MeshLockRenewalThreads>();

    protected MeshLocksBase(IServiceProvider services)
    {
        Services = services;
        _debugLog = DebugMode ? new LazySlim<ILogger?>(Services.LogFor(GetType()).IfEnabled(LogLevel.Debug)) : null;
    }

    public virtual async Task<MeshLockHolder?> TryLock(
        string key,
        MeshLockOptions? lockOptions,
        CancellationToken cancellationToken = default)
    {
        lockOptions ??= LockOptions;
        var fullKey = GetFullKey(key);
        var holder = CreateHolder(key, lockOptions, cancellationToken);
        DebugLog?.LogDebug("TryLock: {Key} = {Id}", fullKey, holder.Id);
        try {
            cancellationToken.ThrowIfCancellationRequested();
            var isAcquired = await TryLock(key, holder.Id, lockOptions.ExpirationPeriod, cancellationToken)
                .ConfigureAwait(false);
            if (!isAcquired)
                return null;
        }
        catch (Exception e) {
            if (e is OperationCanceledException)
                DebugLog?.LogDebug("TryLock cancelled: {Key} = {Id}", fullKey, holder.Id);
            else
                DebugLog?.LogError(e, "TryLock failed: {Key} = {Id}", fullKey, holder.Id);
            throw;
        }
        holder.Start();
        return holder;
    }

    public virtual async Task<MeshLockHolder> Lock(
        string key,
        MeshLockOptions? lockOptions,
        CancellationToken cancellationToken = default)
    {
        lockOptions ??= LockOptions;
        var fullKey = GetFullKey(key);
        var warningDelay = lockOptions.WarningDelay.Positive();
        var warningDelayTask = warningDelay > TimeSpan.Zero
            ? Clock.Delay(warningDelay, cancellationToken)
            : null;
        var holder = CreateHolder(key, lockOptions, cancellationToken);
        DebugLog?.LogDebug("Lock: {Key} = {Id}", fullKey, holder.Id);
        IAsyncSubscription<string>? changes = null;
        var changesCts = cancellationToken.CreateLinkedTokenSource();
        try {
            var consumeTask = (Task<bool>?)null;
            while (true) {
                try {
                    changes ??= await Changes(key, changesCts.Token).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    var tryLockTask = TryLock(key, holder.Id, lockOptions.ExpirationPeriod, cancellationToken);
                    if (warningDelayTask != null) {
                        var completedTask = await Task.WhenAny(tryLockTask, warningDelayTask).ConfigureAwait(false);
                        if (completedTask == warningDelayTask) {
                            if (warningDelayTask.IsCompletedSuccessfully)
                                Log?.LogWarning("Lock takes too long: {Key} = {Id}", fullKey, holder.Id);
                            warningDelayTask = null; // We report it just once per Lock call
                        }
                    }
                    var isAcquired = await tryLockTask.ConfigureAwait(false);
                    if (isAcquired)
                        break;
                }
                catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
                    continue;
                }

                try {
                    consumeTask ??= changes.Reader.WaitToReadAndConsumeAsync(CancellationToken.None);
                    var canRead = await consumeTask
                        .WaitAsync(lockOptions.UnconditionalCheckPeriod, cancellationToken)
                        .ConfigureAwait(false);
                    // It's important to throw on cancellation here: canRead may return false exactly due to this
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!canRead)
                        throw new OperationCanceledException("Subscription to changes is lost.");
                    consumeTask = null;
                }
                catch (TimeoutException) { }
                catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
                    await changes.DisposeSilentlyAsync().ConfigureAwait(false);
                    changes = null;
                    consumeTask = null;
                }
            }
        }
        catch (Exception e) {
            if (e.IsCancellationOf(cancellationToken))
                DebugLog?.LogDebug("Lock cancelled: {Key} = {Id}", fullKey, holder.Id);
            else
                Log?.LogError(e, "Lock failed: {Key} = {Id}", fullKey, holder.Id);
            throw;
        }
        finally {
            changesCts.CancelAndDisposeSilently();
            await changes.DisposeSilentlyAsync().ConfigureAwait(false);
        }
        holder.Start();
        return holder;
    }

    public abstract string GetFullKey(string key);
    public abstract Task<MeshLockInfo?> GetInfo(string key, CancellationToken cancellationToken = default);
    public abstract Task<IAsyncSubscription<string>> Changes(string key, CancellationToken cancellationToken = default);
    public abstract Task<List<string>> ListKeys(string prefix, CancellationToken cancellationToken = default);
    public abstract IMeshLocks With(string keyPrefix, MeshLockOptions? lockOptions);

    bool IMeshLocksBackend.TryRenewBlocking(string key, string value, TimeSpan expiresIn, CancellationToken cancellationToken)
        => TryRenewBlocking(key, value, expiresIn, cancellationToken);
    Task<MeshLockReleaseResult> IMeshLocksBackend.TryRelease(string key, string value, CancellationToken cancellationToken)
        => TryRelease(key, value, cancellationToken);
    Task<bool> IMeshLocksBackend.ForceRelease(string key, bool mustNotify, CancellationToken cancellationToken)
        => ForceRelease(key, mustNotify, cancellationToken);

    // Protected methods

    protected virtual MeshLockHolder CreateHolder(string key,
        MeshLockOptions options,
        CancellationToken cancellationToken)
    {
        var holderId = NextHolderId();
        var lockToken = options.LinkCancellationToken ? cancellationToken : default;
        return new (this, holderId, key, options, lockToken);
    }

    protected virtual string NextHolderId()
        => string.Concat(HolderKeyPrefix, Interlocked.Increment(ref LastHolderId).ToString());

    protected abstract Task<bool> TryLock(string key, string value, TimeSpan expiresIn, CancellationToken cancellationToken);
    protected abstract bool TryRenewBlocking(string key, string value, TimeSpan expiresIn, CancellationToken cancellationToken);
    protected abstract Task<MeshLockReleaseResult> TryRelease(string key, string value, CancellationToken cancellationToken);
    protected abstract Task<bool> ForceRelease(string key, bool mustNotify, CancellationToken cancellationToken);
}
