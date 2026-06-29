namespace ActualChat.UI.Blazor.App.Services;

public abstract class LocationTrackerBase : UIServiceBase<AppUIHub>, ILocationTracker
{
    private readonly MutableState<GeoPoint?> _lastKnown;

    public IState<GeoPoint?> LastKnown => _lastKnown;
    protected bool IsTracking { get; set; }

    protected LocationTrackerBase(AppUIHub hub) : base(hub)
        => _lastKnown = hub.StateFactory.NewMutable(
            (GeoPoint?)null,
            StateCategories.Get(GetType(), nameof(LastKnown)));

    public abstract Task<GeoPoint?> Get(CancellationToken cancellationToken);
    public abstract Task Start(CancellationToken cancellationToken);
    public abstract Task Stop(CancellationToken cancellationToken);

    protected Task<GeoTrackingAccuracy> GetAccuracy(CancellationToken cancellationToken)
        => Hub.LocalSettings.LocalAppSettings().Get(x => x.LocationAccuracyOrDefault, cancellationToken);

    // TODO: is it worth having a separate method?
    protected void SetLocation(GeoPoint? point)
        => _lastKnown.Value = point;
}
