namespace ActualChat.UI.Blazor.App.Services;

public abstract class LocationTrackerBase : ILocationTracker
{
    private readonly MutableState<GeoPoint?> _lastKnown;

    public IState<GeoPoint?> LastKnown => _lastKnown;
    protected bool IsTracking { get; set; }

    protected LocationTrackerBase(AppUIHub hub)
        => _lastKnown = hub.StateFactory.NewMutable(
            (GeoPoint?)null,
            StateCategories.Get(GetType(), nameof(LastKnown)));

    public abstract Task<GeoPoint?> Get(CancellationToken cancellationToken);
    public abstract Task Start(CancellationToken cancellationToken);
    public abstract Task Stop(CancellationToken cancellationToken);

    protected void SetLocation(GeoPoint? point)
        => _lastKnown.Value = point;
}
