namespace ActualChat.UI.Blazor.App.Services;

public abstract class LocationTrackerBase : UIServiceBase<AppUIHub>, ILocationTracker
{
    private readonly MutableState<GeoFix?> _cachedFix;
    private readonly MutableState<GeoTrackingError?> _error;

    protected bool IsTracking { get; set; }
    public IState<GeoTrackingError?> Error => _error;

    protected LocationTrackerBase(AppUIHub hub) : base(hub)
    {
        _cachedFix = hub.StateFactory.NewMutable(
            (GeoFix?)null,
            StateCategories.Get(GetType(), nameof(_cachedFix)));
        _error = hub.StateFactory.NewMutable(
            (GeoTrackingError?)null,
            StateCategories.Get(GetType(), nameof(Error)));
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

    // Protected/internal methods

    protected abstract Task<GeoFix?> Fetch(bool mustBeFresh, CancellationToken cancellationToken);

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
}
