using ActualLab.Diagnostics;
using ActualLab.Resilience;

namespace ActualChat.Mesh;

public abstract class MeshLocksBase : IMeshLocksBackend
{
    private static bool DebugMode => Constants.DebugMode.MeshLocks;

    private readonly LazySlim<ILogger?>? _debugLog;

    protected readonly string HolderKeyPrefix = Alphabet.AlphaNumeric.Generator8.Next() + "-";
    protected long LastHolderId;

    protected ILogger Log => field ??= Services.LogFor(GetType());
    protected ILogger? DebugLog => _debugLog?.Value;
    protected ChaosMaker ChaosMaker => field ??= Services.GetRequiredService<ChaosMaker>();

    public MeshLockOptions LockOptions { get; init; } = MeshLockOptions.Default;
    public RetryDelaySeq RetryDelays { get; init; } = RetryDelaySeq.Exp(0.5, 10);

    public IServiceProvider Services { get; init; }
    public MomentClock Clock => field ??= Services.Clocks().SystemClock;
    public IMeshLocksBackend Backend => this;

    // IMeshLocksBackend
    ILogger IMeshLocksBackend.Log => Log;
    ILogger? IMeshLocksBackend.DebugLog => DebugLog;
    ChaosMaker IMeshLocksBackend.ChaosMaker => ChaosMaker;

    protected MeshLocksBase(IServiceProvider services)
    {
        Services = services;
        _debugLog = DebugMode ? new LazySlim<ILogger?>(Log.IfEnabled(LogLevel.Debug)) : null;
    }

    public virtual async Task<MeshLockHolder?> TryLock(
        string key,
        MeshLockOptions? lockOptions,
        CancellationToken cancellationToken = default)
    {
        lockOptions ??= LockOptions;
        var holder = CreateHolder(key, lockOptions, cancellationToken);
        DebugLog?.LogDebug("TryLock: {Key} = {Id}", key, holder.Id);
        try {
            cancellationToken.ThrowIfCancellationRequested();
            var isAcquired = await TryLock(key, holder.Id, lockOptions.ExpirationPeriod, cancellationToken)
                .ConfigureAwait(false);
            if (!isAcquired)
                return null;
        }
        catch (Exception e) {
            if (e is OperationCanceledException)
                DebugLog?.LogDebug("TryLock cancelled: {Key} = {Id}", key, holder.Id);
            else
                DebugLog?.LogError(e, "TryLock failed: {Key} = {Id}", key, holder.Id);
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
        var warningDelay = lockOptions.WarningDelay.Positive();
        var warningDelayTask = warningDelay > TimeSpan.Zero
            ? Clock.Delay(warningDelay, cancellationToken)
            : null;
        var holder = CreateHolder(key, lockOptions, cancellationToken);
        DebugLog?.LogDebug("Lock: {Key} = {Id}", key, holder.Id);
        IAsyncSubscription<string>? changes = null;
        try {
            var consumeTask = (Task<bool>?)null;
            while (true) {
                try {
                    changes ??= await Changes(key, cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    var tryLockTask = TryLock(key, holder.Id, lockOptions.ExpirationPeriod, cancellationToken);
                    if (warningDelayTask != null) {
                        var completedTask = await Task.WhenAny(tryLockTask, warningDelayTask).ConfigureAwait(false);
                        if (completedTask == warningDelayTask) {
                            if (warningDelayTask.IsCompletedSuccessfully)
                                Log.LogWarning("Lock takes too long: {Key} = {Id}", key, holder.Id);
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
                DebugLog?.LogDebug("Lock cancelled: {Key} = {Id}", key, holder.Id);
            else
                Log.LogError(e, "Lock failed: {Key} = {Id}", key, holder.Id);
            throw;
        }
        finally {
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

    Task<bool> IMeshLocksBackend.TryRenew(string key, string value, TimeSpan expiresIn, CancellationToken cancellationToken)
        => TryRenew(key, value, expiresIn, cancellationToken);
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
        => string.Concat(HolderKeyPrefix, Interlocked.Increment(ref LastHolderId).ToInvariantString());

    protected abstract Task<bool> TryLock(string key, string value, TimeSpan expiresIn, CancellationToken cancellationToken);
    protected abstract Task<bool> TryRenew(string key, string value, TimeSpan expiresIn, CancellationToken cancellationToken);
    protected abstract Task<MeshLockReleaseResult> TryRelease(string key, string value, CancellationToken cancellationToken);
    protected abstract Task<bool> ForceRelease(string key, bool mustNotify, CancellationToken cancellationToken);
}
