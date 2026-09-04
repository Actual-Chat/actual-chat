using ActualChat.Users.AppStores;
using ActualChat.Users.Module;

namespace ActualChat.Users;

/// <summary>
/// Probes the stores for the app kinds clients actually asked about, backing off
/// exponentially and dropping a kind for good once its release is published.
/// </summary>
public sealed class AppUpdateProber : ActivatedWorkerBase
{
    private readonly ConcurrentDictionary<AppKind, ProbeState> _entries = new();

    private AppUpdateSettings Settings { get; }
    private AppUpdateStore Store { get; }
    private StoreProbes Probes { get; }
    private MomentClockSet Clocks { get; }
    private AppUpdates AppUpdates => field ??= (AppUpdates)Services.GetRequiredService<IAppUpdates>();

    public AppUpdateProber(IServiceProvider services) : base(services)
    {
        Settings = services.GetRequiredService<UsersSettings>().AppUpdates;
        Store = services.GetRequiredService<AppUpdateStore>();
        Probes = services.GetRequiredService<StoreProbes>();
        Clocks = services.Clocks();
        // Entries come due on their own backoff schedule, so the loop has to wake up
        // periodically even when no client asks for anything new.
        UnconditionalActivationPeriod = TimeSpan.FromSeconds(30).ToRandom(0.1);
    }

    public void Request(AppKind appKind)
    {
        // The caller re-asks once per RecheckPeriod while a kind is unsettled, so waking the loop
        // on every call is what keeps a backed-off entry from waiting out the whole idle period.
        _entries.TryAdd(appKind, new ProbeState { DueAt = Clocks.SystemClock.Now });
        Activate();
    }

    // Protected/internal methods

    // It's internal to be accessible from tests: an entry carries a backoff deadline, and a
    // test that reuses an app kind must not inherit the one the previous test left behind
    internal void Forget(AppKind appKind)
        => Drop(appKind);

    protected override async Task<bool> OnActivate(CancellationToken cancellationToken)
    {
        foreach (var (appKind, state) in _entries) {
            if (state.DueAt > Clocks.SystemClock.Now)
                continue;

            try {
                await ProbeOne(appKind, state, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException) {
                Log.LogWarning(e, "Probe of {AppKind} failed", appKind);
                Postpone(state);
            }
        }

        return true; // Always "done for now" - the next due entry is picked up on the next wake-up
    }

    // Private methods

    private async Task ProbeOne(AppKind appKind, ProbeState state, CancellationToken cancellationToken)
    {
        var ownVersion = ApiConstants.BuildVersion;
        var record = await Store.Get(appKind, cancellationToken).ConfigureAwait(false);
        if (record?.Info is { } info && VersionExt.ParseBuildVersion(info.Version) >= ownVersion) {
            Drop(appKind);
            return;
        }

        var storeId = Settings.GetStoreId(appKind);
        var probe = Probes.Get(appKind);
        if (probe is null || storeId.IsNullOrEmpty()) {
            Drop(appKind);
            return;
        }

        // A throttle rather than a lock: probes are idempotent, so two nodes racing once is harmless
        var isProbeAllowed = await Store
            .TryStartProbe(appKind, Settings.MinProbeInterval, cancellationToken)
            .ConfigureAwait(false);
        if (!isProbeAllowed) {
            state.DueAt = Clocks.SystemClock.Now + Settings.MinProbeInterval;
            return;
        }

        var result = await probe.Probe(storeId, cancellationToken).ConfigureAwait(false);
        var now = Clocks.SystemClock.Now;
        if (result is not { } probeResult) {
            Log.LogInformation("{AppKind} isn't listed in the store", appKind);
            Postpone(state);
            return;
        }

        var published = probeResult.BuildVersion is { } storeBuildVersion
            ? GetPublishedFromBuildVersion(appKind, probeResult, storeBuildVersion, ownVersion, now)
            : GetPublishedFromTrain(appKind, probeResult, record, ownVersion, now);
        if (published is null) {
            var baseline = record is null
                ? new AppUpdateRecord(null, probeResult.StoreVersion, probeResult.ReleasedAt ?? now, now)
                : record with {
                    LastSeenStoreVersion = probeResult.StoreVersion,
                    LastSeenReleasedAt = probeResult.ReleasedAt ?? now,
                    ProbedAt = now,
                };
            await Store.Set(appKind, baseline, cancellationToken).ConfigureAwait(false);
            Postpone(state);
            return;
        }

        // The release this one replaces is what clients keep seeing until the announce delay
        // is out, so a client behind the previous release doesn't lose its banner meanwhile.
        var newRecord = new AppUpdateRecord(
            published, probeResult.StoreVersion, probeResult.ReleasedAt ?? now, now, record?.Info);
        await Store.Set(appKind, newRecord, cancellationToken).ConfigureAwait(false);
        Log.LogInformation("{AppKind} {Version} is published (store version: {StoreVersion})",
            appKind, published.Version, published.StoreVersion);
        Drop(appKind);
        AppUpdates.Invalidate(appKind);
    }

    private static AppUpdateInfo? GetPublishedFromBuildVersion(
        AppKind appKind,
        StoreProbeResult result,
        Version storeBuildVersion,
        Version ownVersion,
        Moment now)
        // A store that shows the build version needs no history: it either has it or it doesn't.
        // A store ahead of the server (a rollback) still moves the record forward, which is right.
        => storeBuildVersion >= ownVersion
            ? new AppUpdateInfo(appKind, storeBuildVersion.ToString(), result.StoreVersion,
                result.ReleasedAt ?? now, now)
            : null;

    private static AppUpdateInfo? GetPublishedFromTrain(
        AppKind appKind,
        StoreProbeResult result,
        AppUpdateRecord? record,
        Version ownVersion,
        Moment now)
    {
        // A marketing version can't be compared with a build version, so this detects a *change*
        // on the server's train; without a baseline "2.17" could be 2.17.100, so the first probe
        // only records one.
        var releasedAt = result.ReleasedAt ?? now;
        if (record is null)
            return null;

        var hasChanged = record.LastSeenStoreVersion != result.StoreVersion
            || record.LastSeenReleasedAt != releasedAt;
        if (!hasChanged)
            return null;

        var storeTrain = VersionExt.ParseBuildVersion(result.StoreVersion);
        var ownTrain = new Version(ownVersion.Major, ownVersion.Minor, 0);
        if (new Version(storeTrain.Major, storeTrain.Minor, 0) < ownTrain)
            return null;

        return new AppUpdateInfo(
            appKind, ownVersion.ToString(), result.StoreVersion, releasedAt, now);
    }

    private void Postpone(ProbeState state)
        => state.DueAt = Clocks.SystemClock.Now + Settings.ProbeDelays[++state.TryIndex];

    private void Drop(AppKind appKind)
        => _entries.TryRemove(appKind, out _);

    // Nested types

    private sealed class ProbeState
    {
        public Moment DueAt { get; set; }
        public int TryIndex { get; set; }
    }
}
