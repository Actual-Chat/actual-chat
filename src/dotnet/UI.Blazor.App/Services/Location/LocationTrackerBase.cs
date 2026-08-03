namespace ActualChat.UI.Blazor.App.Services;

public abstract class LocationTrackerBase : UIServiceBase<AppUIHub>, ILocationTracker
{
    private readonly MutableState<GeoPoint?> _trackedLocation;
    private readonly MutableState<GeoTrackingError?> _error;

    public IState<GeoTrackingError?> Error => _error;
    protected bool IsTracking { get; set; }

    protected LocationTrackerBase(AppUIHub hub) : base(hub)
    {
        _trackedLocation = hub.StateFactory.NewMutable(
            (GeoPoint?)null,
            StateCategories.Get(GetType(), nameof(_trackedLocation)));
        _error = hub.StateFactory.NewMutable(
            (GeoTrackingError?)null,
            StateCategories.Get(GetType(), nameof(Error)));
    }

    public async Task<GeoPoint?> Get(bool mustBeFresh = false, CancellationToken cancellationToken = default)
    {
        // Use() binds the calling compute method to the tracked position, so every fix recomputes it.
        var trackedLocation = await _trackedLocation.Use(cancellationToken).ConfigureAwait(false);
        if (!mustBeFresh)
            return trackedLocation ?? await FetchCurrent(false, cancellationToken).ConfigureAwait(false);

        var isTrackingHealthy = IsTracking && _error.Value is null;
        if (isTrackingHealthy && await GetTracked(cancellationToken).ConfigureAwait(false) is { } point)
            return point;

        return await FetchCurrent(true, cancellationToken).ConfigureAwait(false);
    }

    public abstract Task Start(CancellationToken cancellationToken);
    public abstract Task Stop(CancellationToken cancellationToken);

    // Protected/internal methods

    protected abstract Task<GeoPoint?> FetchCurrent(bool mustBeFresh, CancellationToken cancellationToken);

    protected Task<GeoTrackingAccuracy> GetAccuracy(CancellationToken cancellationToken)
        => Hub.LocalSettings.LocalAppSettings().Get(x => x.LocationAccuracyOrDefault, cancellationToken);

    protected void SetTrackedLocation(GeoPoint? point)
    {
        _trackedLocation.Value = point;
        if (point is not null)
            _error.Value = null;
    }

    protected void SetError(GeoTrackingError? error)
        => _error.Value = error;

    // Private methods

    private async Task<GeoPoint?> GetTracked(CancellationToken cancellationToken)
    {
        // A running tracker delivers fixes at least as fresh as a one-shot request would,
        // so wait for its next one rather than start a second location session.
        using var cts = cancellationToken.CreateLinkedTokenSource(Constants.Location.GetTimeout);
        try {
            var cTrackedLocation = await _trackedLocation.Computed
                .When(x => x is not null, cts.Token)
                .ConfigureAwait(false);
            return cTrackedLocation.Value;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            return null;
        }
    }
}
