namespace ActualChat.UI.Blazor.App.Services;

public abstract class LocationTrackerBase : UIServiceBase<AppUIHub>, ILocationTracker
{
    private readonly MutableState<GeoPoint?> _cachedLocation;
    private readonly MutableState<GeoTrackingError?> _error;

    public IState<GeoTrackingError?> Error => _error;
    protected bool IsTracking { get; set; }

    protected LocationTrackerBase(AppUIHub hub) : base(hub)
    {
        _cachedLocation = hub.StateFactory.NewMutable(
            (GeoPoint?)null,
            StateCategories.Get(GetType(), nameof(_cachedLocation)));
        _error = hub.StateFactory.NewMutable(
            (GeoTrackingError?)null,
            StateCategories.Get(GetType(), nameof(Error)));
    }

    public async Task<GeoPoint?> Get(bool mustBeFresh = false, CancellationToken cancellationToken = default)
    {
        var trackedLocation = await _cachedLocation.Use(cancellationToken).ConfigureAwait(false);
        if (!mustBeFresh)
            return trackedLocation ?? await Fetch(false, cancellationToken).ConfigureAwait(false);

        var isTrackingHealthy = IsTracking && _error.Value is null;
        if (isTrackingHealthy && await _cachedLocation.Use(cancellationToken).ConfigureAwait(false) is { } point)
            return point;

        var fetched = await Fetch(true, cancellationToken).ConfigureAwait(false);
        if (fetched is not null)
            SetCached(fetched);

        return fetched;
    }

    public abstract Task Start(CancellationToken cancellationToken);
    public abstract Task Stop(CancellationToken cancellationToken);

    // Protected/internal methods

    protected abstract Task<GeoPoint?> Fetch(bool mustBeFresh, CancellationToken cancellationToken);

    protected Task<GeoTrackingAccuracy> GetAccuracy(CancellationToken cancellationToken)
        => Hub.LocalSettings.LocalAppSettings().Get(x => x.LocationAccuracyOrDefault, cancellationToken);

    protected void SetCached(GeoPoint? point)
    {
        _cachedLocation.Value = point;
        if (point is not null)
            _error.Value = null;
    }

    protected void SetError(GeoTrackingError? error)
        => _error.Value = error;
}
