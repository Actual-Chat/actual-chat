using ActualChat.UI.Blazor.App.Services;
using CoreLocation;

namespace ActualChat.App.Maui.Location;

public sealed class AppleLocationTracker : ILocationTracker
{
    private readonly MutableState<GeoPoint?> _lastKnown;
    private CLLocationManager? _manager;

    public IState<GeoPoint?> LastKnown => _lastKnown;
    public bool IsTracking { get; private set; }

    public AppleLocationTracker(AppUIHub hub)
        => _lastKnown = hub.StateFactory.NewMutable(
            (GeoPoint?)null,
            StateCategories.Get(GetType(), nameof(LastKnown)));

    public Task Start(CancellationToken cancellationToken)
    {
        if (IsTracking)
            return Task.CompletedTask;

        IsTracking = true;
        MainThread.BeginInvokeOnMainThread(() => {
            _manager ??= CreateManager();
            _manager.RequestWhenInUseAuthorization();
            _manager.StartUpdatingLocation();
        });
        return Task.CompletedTask;
    }

    public Task Stop(CancellationToken cancellationToken)
    {
        if (!IsTracking)
            return Task.CompletedTask;

        IsTracking = false;
        _lastKnown.Value = null;
        MainThread.BeginInvokeOnMainThread(() => _manager?.StopUpdatingLocation());
        return Task.CompletedTask;
    }

    // Private methods

    private CLLocationManager CreateManager()
    {
        var manager = new CLLocationManager {
            DesiredAccuracy = CLLocation.AccuracyBest,
            AllowsBackgroundLocationUpdates = true,
            PausesLocationUpdatesAutomatically = false,
        };
        manager.LocationsUpdated += OnLocationsUpdated;
        return manager;
    }

    private void OnLocationsUpdated(object? sender, CLLocationsUpdatedEventArgs e)
    {
        var location = e.Locations.LastOrDefault();
        if (location is null)
            return;

        var accuracy = location.HorizontalAccuracy >= 0 ? (float)location.HorizontalAccuracy : (float?)null;
        var bearing = location.Course >= 0 ? (float)location.Course : (float?)null;
        _lastKnown.Value = new GeoPoint(location.Coordinate.Latitude, location.Coordinate.Longitude, accuracy, bearing);
    }
}
