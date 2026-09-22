using ActualLab.Locking;

namespace ActualChat.UI.Blazor.App.Services;

public abstract class LocationTrackerBase : UIServiceBase<AppUIHub>, ILocationTracker
{
    protected const float MinHeadingChange = 5; // Degrees

    private readonly MutableState<GeoFix?> _cachedFix;
    private readonly MutableState<GeoTrackingError?> _error;
    private readonly MutableState<float?> _heading;
    // CheckedPass: a platform hook may run inline on the main thread and re-enter this.
    private readonly AsyncLock _headingLock = new(LockReentryMode.CheckedPass);
    private int _headingWatcherCount;

    private BackgroundStateTracker BackgroundStateTracker
        => field ??= Services.GetRequiredService<BackgroundStateTracker>();

    protected bool IsTracking { get; set; }
    protected GeoFix? CachedFix => _cachedFix.Value;
    public IState<GeoTrackingError?> Error => _error;
    // Kept out of the fix Get returns: it changes orders of magnitude more often, and only the own
    // marker and the reported point use it, while every location message depends on the fix.
    public IState<float?> Heading => _heading;

    protected LocationTrackerBase(AppUIHub hub) : base(hub)
    {
        _cachedFix = hub.StateFactory.NewMutable(
            (GeoFix?)null,
            StateCategories.Get(GetType(), nameof(_cachedFix)));
        _error = hub.StateFactory.NewMutable(
            (GeoTrackingError?)null,
            StateCategories.Get(GetType(), nameof(Error)));
        _heading = hub.StateFactory.NewMutable(
            (float?)null,
            StateCategories.Get(GetType(), nameof(_heading)));
    }

    public async Task<GeoFix?> Get(bool mustBeFresh = false, CancellationToken cancellationToken = default)
    {
        var cachedFix = await _cachedFix.Use(cancellationToken).ConfigureAwait(false);
        if (!mustBeFresh)
            return cachedFix ?? await Fetch(false, cancellationToken).ConfigureAwait(false);

        // Healthy tracking isn't enough: the watch updates once per UpdatePeriod, and keeps stale fixes on failure.
        var isTrackingHealthy = IsTracking && _error.Value is null;
        if (isTrackingHealthy && cachedFix is not null)
            return cachedFix;

        var fetched = await Fetch(true, cancellationToken).ConfigureAwait(false);
        if (fetched is not null)
            SetCached(fetched);

        return fetched;
    }

    public abstract Task Start(CancellationToken cancellationToken);
    public abstract Task Stop(CancellationToken cancellationToken);

    public async Task WatchHeading(CancellationToken cancellationToken)
    {
        var isWatching = false;
        try {
            var changes = BackgroundStateTracker.IsBackground.Computed.Changes(cancellationToken);
            await foreach (var cIsBackground in changes.ConfigureAwait(false)) {
                if (isWatching != cIsBackground.Value)
                    continue;

                isWatching = !isWatching;
                await UpdateHeadingWatcherCount(isWatching ? 1 : -1).ConfigureAwait(false);
            }
        }
        finally {
            if (isWatching)
                await UpdateHeadingWatcherCount(-1).ConfigureAwait(false);
        }
    }

    // Protected/internal methods

    protected abstract Task<GeoFix?> Fetch(bool mustBeFresh, CancellationToken cancellationToken);

    protected virtual Task StartHeadingUpdates()
        => Task.CompletedTask;

    protected virtual Task StopHeadingUpdates()
        => Task.CompletedTask;

    protected Task<GeoTrackingAccuracy> GetAccuracy(CancellationToken cancellationToken)
        => Hub.LocalSettings.LocalAppSettings().Get(x => x.LocationAccuracyOrDefault, cancellationToken);

    protected void SetCached(GeoFix? fix)
    {
        _cachedFix.Value = fix;
        if (fix is not null)
            _error.Value = null;
    }

    protected void SetError(GeoTrackingError? error)
        => _error.Value = error;

    protected void SetHeading(float? heading)
    {
        // Unlocked: platform callbacks are already serialized (the main thread, or the JS interop
        // one), and the count is read here only to drop the ones still in flight past a stop.
        if (Volatile.Read(ref _headingWatcherCount) == 0)
            return;

        if (heading is { } h)
            heading = ((h % 360) + 360) % 360;
        // Drops sub-threshold changes: each accepted one re-renders every map showing the own marker.
        if (heading is { } newHeading && _heading.Value is { } oldHeading
            && GetAngleDistance(newHeading, oldHeading) < MinHeadingChange)
            return;

        _heading.Value = heading;
    }

    // Private methods

    private async Task UpdateHeadingWatcherCount(int delta)
    {
        // The lock orders the platform calls, so a stop can't overtake the start it ends.
        using var _ = await _headingLock.Lock(CancellationToken.None).ConfigureAwait(false);
        var count = _headingWatcherCount + delta;
        Volatile.Write(ref _headingWatcherCount, count);
        try {
            if (delta > 0 && count == 1)
                await StartHeadingUpdates().ConfigureAwait(false);
            else if (delta < 0 && count == 0) {
                await StopHeadingUpdates().ConfigureAwait(false);
                _heading.Value = null;
            }
        }
        catch (Exception e) {
            // Best-effort extra: a dead compass mustn't take the share reporting down with it.
            Log.LogWarning(e, "Failed to toggle heading updates");
        }
    }

    private static float GetAngleDistance(float a, float b)
    {
        var distance = Math.Abs(a - b) % 360;
        return distance > 180 ? 360 - distance : distance;
    }
}
