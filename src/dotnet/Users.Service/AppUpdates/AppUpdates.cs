using ActualChat.Users.Module;

namespace ActualChat.Users;

/// <summary>
/// Answers "is there a newer build in this user's store?" from the Redis record the
/// prober maintains. It never probes inline - it only asks the prober to work on a kind.
/// </summary>
public class AppUpdates : IAppUpdates
{
    private IServiceProvider Services { get; }
    private HostInfo HostInfo { get; }
    private AppUpdateSettings Settings { get; }
    private AppUpdateStore Store { get; }
    private MomentClockSet Clocks { get; }
    private Moment StartedAt { get; }
    private AppUpdateProber Prober => field ??= Services.GetRequiredService<AppUpdateProber>();
    private ILogger Log => field ??= Services.LogFor(GetType());

    public AppUpdates(IServiceProvider services)
    {
        Services = services;
        HostInfo = services.HostInfo();
        Settings = services.GetRequiredService<UsersSettings>().AppUpdates;
        Store = services.GetRequiredService<AppUpdateStore>();
        Clocks = services.Clocks();
        // The web grace is measured from here rather than from process start: this service is
        // constructed on the node's first update query, which is close enough and testable.
        StartedAt = Clocks.SystemClock.Now;
    }

    // [ComputeMethod]
    public virtual async Task<AppUpdateInfo?> GetLatestUpdateInfo(
        AppKind appKind,
        CancellationToken cancellationToken)
    {
        if (Settings.Overrides.TryGetValue(appKind.ToString(), out var overrideVersion)
            && VersionExt.TryParseBuildVersion(overrideVersion, out var overrideBuildVersion)) {
            var now = Clocks.SystemClock.Now;
            return new AppUpdateInfo(appKind, overrideBuildVersion.ToString(), overrideVersion, now, now);
        }
        if (!(Settings.IsEnabled ?? HostInfo.IsProductionInstance))
            return null;
        if (appKind == AppKind.Wasm)
            return GetWasmUpdateInfo();
        if (Settings.GetStoreId(appKind).IsNullOrEmpty())
            return null;

        var record = await Store.Get(appKind, cancellationToken).ConfigureAwait(false);
        var info = record?.Info;
        if (info is not null && IsPending(info, out var announceAt)) {
            // A detected release is held back for AnnounceDelay, which is what absorbs the lag
            // between the storefront we probe and the one this user is served. Clients behind
            // the previous release keep their banner; nobody hears about the new one yet.
            Computed.GetCurrent().InvalidateSafely(announceAt - Clocks.SystemClock.Now);
            return record!.PreviousInfo;
        }
        if (info is not null && VersionExt.ParseBuildVersion(info.Version) >= ApiConstants.BuildVersion)
            return info; // Settled: a published release is assumed to stay published

        // The server is ahead of the store, so this kind needs probing, and this node has to
        // re-read Redis until some node settles it - nothing else tells it the record changed.
        Prober.Request(appKind);
        Computed.GetCurrent().InvalidateSafely(Settings.RecheckPeriod);
        return info;
    }

    public void Invalidate(AppKind appKind)
    {
        using (Invalidation.Begin())
            _ = GetLatestUpdateInfo(appKind, default);
    }

    // Private methods

    private bool IsPending(AppUpdateInfo info, out Moment announceAt)
    {
        announceAt = info.DetectedAt + Settings.AnnounceDelay;
        return Clocks.SystemClock.Now < announceAt;
    }

    private AppUpdateInfo? GetWasmUpdateInfo()
    {
        // Each node answers for itself: during a rolling deploy an old pod must not send its
        // clients to a reload that can land right back on an old pod.
        var readyAt = StartedAt + Settings.WasmGracePeriod;
        var now = Clocks.SystemClock.Now;
        if (now < readyAt) {
            Computed.GetCurrent().InvalidateSafely(readyAt - now);
            return null;
        }

        return new AppUpdateInfo(
            AppKind.Wasm,
            ApiConstants.BuildVersion.ToString(),
            ApiConstants.FullVersionString,
            StartedAt,
            StartedAt);
    }
}
