namespace ActualChat.UI.Blazor.App.Services;

public abstract class LocationTrackerBase : UIServiceBase<AppUIHub>, ILocationTracker
{
    private readonly MutableState<GeoPoint?> _lastKnown;
    private readonly MutableState<GeoTrackingError?> _error;

    public IState<GeoPoint?> LastKnown => _lastKnown;
    public IState<GeoTrackingError?> Error => _error;
    protected bool IsTracking { get; set; }

    protected LocationTrackerBase(AppUIHub hub) : base(hub)
    {
        _lastKnown = hub.StateFactory.NewMutable(
            (GeoPoint?)null,
            StateCategories.Get(GetType(), nameof(LastKnown)));
        _error = hub.StateFactory.NewMutable(
            (GeoTrackingError?)null,
            StateCategories.Get(GetType(), nameof(Error)));
    }

    public abstract Task<GeoPoint?> Get(bool force = false, CancellationToken cancellationToken = default);
    public abstract Task Start(CancellationToken cancellationToken);
    public abstract Task Stop(CancellationToken cancellationToken);

    protected Task<GeoTrackingAccuracy> GetAccuracy(CancellationToken cancellationToken)
        => Hub.LocalSettings.LocalAppSettings().Get(x => x.LocationAccuracyOrDefault, cancellationToken);

    protected void SetLocation(GeoPoint? point)
    {
        _lastKnown.Value = point;
        if (point is not null)
            _error.Value = null;
    }

    protected void SetError(GeoTrackingError? error)
        => _error.Value = error;
}
