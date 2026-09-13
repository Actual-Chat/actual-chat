using ActualChat.Redis;
using ActualChat.Users.Db;
using ActualChat.Users.Module;
using ActualLab.Redis;

namespace ActualChat.Users;

/// <summary>
/// Answers "is there a newer build in this user's store?" from a Redis record, and keeps that
/// record current by re-checking the store for as long as it's behind this server.
/// </summary>
public class AppUpdates : IAppUpdates
{
    private IServiceProvider Services { get; }
    private AppUpdateSettings Settings { get; }
    private HostInfo HostInfo { get; }
    private AppStoreProbes Probes { get; }
    private RedisDb RedisDb { get; } // One record per app kind, no TTL, no DB and no backend
    private IMeshLocks StoreLocks => field ??= Services.MeshLocks().WithKeyPrefix(nameof(AppUpdates));
    private MomentClockSet Clocks { get; }
    private ILogger Log => field ??= Services.LogFor(GetType());

    private Moment StartedAt { get; }
    private Moment SystemNow => Clocks.SystemClock.Now;

    public AppUpdates(IServiceProvider services)
    {
        Services = services;
        Settings = services.GetRequiredService<UsersSettings>().AppUpdates;
        HostInfo = services.HostInfo();
        Probes = services.GetRequiredService<AppStoreProbes>();
        RedisDb = services.GetRequiredService<RedisDb<UsersDbContext>>().WithKeyPrefix("AppUpdates");
        Clocks = services.Clocks();
        // The web grace and the recheck schedule are measured from here: this service is
        // constructed on the node's first update query, close enough to the deploy, and testable.
        StartedAt = SystemNow;
    }

    // [ComputeMethod(ConsolidationDelay = 0)]
    public virtual async Task<AppUpdateInfo?> GetLatestUpdateInfo(
        AppKind appKind,
        CancellationToken cancellationToken)
    {
        var now = SystemNow;
        var ownVersion = ApiConstants.BuildVersion;
        if (Settings.Overrides.TryGetValue(appKind.ToString(), out var overrideVersion)
            && VersionExt.TryParseBuildVersion(overrideVersion, out var overrideBuildVersion))
            return new AppUpdateInfo(appKind, overrideBuildVersion.ToString(), overrideVersion, now, now);

        if (!(Settings.IsEnabled ?? HostInfo.IsProductionInstance))
            return null;
        if (appKind == AppKind.Wasm)
            return GetWasmUpdateInfo();
        if (Settings.GetStoreId(appKind).IsNullOrEmpty())
            return null;

        var lastCachedInfo = await GetCachedStoreUpdateInfo(appKind, cancellationToken).ConfigureAwait(false);
        var lastAppUpdateInfo = lastCachedInfo?.Info;
        var lastVersion = lastAppUpdateInfo?.Version ?? VersionExt.Zero;
        if (lastAppUpdateInfo is not null && lastVersion >= ownVersion)
            return lastAppUpdateInfo; // Settled: the store serves everything this server has

        // Play Store publishes before the other stores, so while it's behind we can await for this record's update
        if (appKind != AppKind.Android && !Settings.GetStoreId(AppKind.Android).IsNullOrEmpty()) {
            var androidInfo = await GetLatestUpdateInfo(AppKind.Android, cancellationToken)
                .ConfigureAwait(false);
            if ((androidInfo?.Version ?? VersionExt.Zero) < ownVersion)
                // We depend on Play Store's record, so we'll be invalidated when it updates
                return lastAppUpdateInfo;
        }

        var computed = Computed.GetCurrent();
        _ = BackgroundTask.Run(async () => {
            CachedUpdateInfo cachedInfo;
            try {
                cachedInfo = await ProbeCached(appKind, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception e) {
                Log.LogWarning(e, "Store check of {AppKind} failed", appKind);
                computed.InvalidateSafely(GetRecheckPeriod());
                return;
            }
            var version = cachedInfo.Info?.Version ?? VersionExt.Zero;
            var delay = version > lastVersion
                ? TimeSpan.Zero // New version -> invalidate instantly
                : cachedInfo.NextCheckAt - SystemNow;
            computed.InvalidateSafely(delay);
        }, CancellationToken.None);
        return lastAppUpdateInfo;
    }

    // Internal methods (used by tests)

    internal void Invalidate(AppKind appKind)
    {
        using (Invalidation.Begin())
            _ = GetLatestUpdateInfo(appKind, default);
    }

    internal async Task<CachedUpdateInfo?> GetCachedStoreUpdateInfo(
        AppKind appKind,
        CancellationToken cancellationToken)
    {
        try {
            return await RedisDb
                .Get<CachedUpdateInfo>(appKind.ToString(), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Can't read the cached {AppKind} store update info", appKind);
            return null; // Old format -> deserialization error -> return "nothing cached"
        }
    }

    internal Task SetCachedStoreUpdateInfo(
        AppKind appKind,
        CachedUpdateInfo info,
        CancellationToken cancellationToken)
        => RedisDb.Set(appKind.ToString(), info, cancellationToken);

    internal async Task RemoveCachedStoreUpdateInfo(AppKind appKind, CancellationToken cancellationToken)
    {
        // RedisDb.Database applies KeyPrefix, so the key goes in unprefixed here
        var database = await RedisDb.Database.Get(cancellationToken).ConfigureAwait(false);
        await database.KeyDeleteAsync(appKind.ToString()).ConfigureAwait(false);
    }

    // Private methods

    private AppUpdateInfo? GetWasmUpdateInfo()
    {
        // Each node answers for itself: during a rolling deploy an old pod must not send its
        // clients to a reload that can land right back on an old pod.
        var readyAt = StartedAt + Settings.WasmGracePeriod;
        var now = SystemNow;
        if (now >= readyAt)
            return new AppUpdateInfo(
                AppKind.Wasm,
                ApiConstants.BuildVersion.ToString(),
                ApiConstants.FullVersionString,
                StartedAt,
                StartedAt);

        Computed.GetCurrent().InvalidateSafely(readyAt - now);
        return null;
    }

    private async Task<CachedUpdateInfo> ProbeCached(AppKind appKind, CancellationToken cancellationToken)
    {
        // Beginning of double-check locking pattern
        var cachedInfo = await GetCachedStoreUpdateInfo(appKind, cancellationToken).ConfigureAwait(false);
        var now = SystemNow;
        if (cachedInfo is not null && now < cachedInfo.NextCheckAt)
            return cachedInfo; // Another node checked while this one waited

        var storeLock = await StoreLocks.Lock(appKind.ToString(), cancellationToken).ConfigureAwait(false);
        await using var _ = storeLock.ConfigureAwait(false);

        cachedInfo = await GetCachedStoreUpdateInfo(appKind, cancellationToken).ConfigureAwait(false);
        now = SystemNow;
        if (cachedInfo is not null && now < cachedInfo.NextCheckAt)
            return cachedInfo; // Another node checked while this one waited
        // End of double-check locking pattern

        var probe = Probes.Get(appKind);
        if (probe is null) // No store to ask - the kind has none, or a test scripted none
            return (cachedInfo ?? new CachedUpdateInfo(null)) with { NextCheckAt = GetNextCheckAt() };

        var probeResult = await probe.Invoke(Settings.GetStoreId(appKind), cancellationToken).ConfigureAwait(false);
        now = SystemNow;
        var info = cachedInfo?.Info;
        var pendingInfo = cachedInfo?.PendingInfo;
        var lastAppVersion = (pendingInfo ?? info)?.Version ?? VersionExt.Zero;
        if (probeResult.Version > lastAppVersion) {
            pendingInfo = new AppUpdateInfo(appKind, probeResult.VersionString, probeResult.ReleasedAt ?? now, now);
            Log.LogInformation("{AppKind} {Version} is published", appKind, pendingInfo.VersionString);
        }

        // A detection is held back for AnnounceDelay
        var nextCheckAt = GetNextCheckAt();
        if (pendingInfo is not null) {
            var announceAt = pendingInfo.DetectedAt + Settings.AnnounceDelay;
            if (now >= announceAt) { // Must announce
                info = pendingInfo;
                pendingInfo = null;
            }
            else if (announceAt < nextCheckAt)
                nextCheckAt = announceAt;
        }

        var result = new CachedUpdateInfo(info, pendingInfo, nextCheckAt);
        await SetCachedStoreUpdateInfo(appKind, result, cancellationToken).ConfigureAwait(false);
        return result;
    }

    private Moment GetNextCheckAt()
        => SystemNow + GetRecheckPeriod();

    private TimeSpan GetRecheckPeriod()
        // Measured from node start, i.e. from the deploy that opened the gap with the store
        => Settings.GetRecheckPeriod(SystemNow - StartedAt);

    // Nested types

    /// <summary>
    /// What Redis holds per app kind: the announced build (<see cref="Info"/> - what clients are
    /// told, always), the one detected but still waiting out the announce delay
    /// (<see cref="PendingInfo"/>), and when the next check is due.
    /// </summary>
    [DataContract, MessagePackObject]
    public sealed partial record CachedUpdateInfo(
        [property: DataMember, Key(0)] AppUpdateInfo? Info,
        [property: DataMember, Key(1)] AppUpdateInfo? PendingInfo = null,
        // Every node re-reads at this moment, so they check in step rather than each on its own timer
        [property: DataMember, Key(2)] Moment NextCheckAt = default);
}
