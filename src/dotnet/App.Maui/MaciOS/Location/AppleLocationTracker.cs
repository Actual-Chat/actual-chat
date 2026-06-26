using ActualChat.UI.Blazor.App.Services;
using CoreLocation;

namespace ActualChat.App.Maui.Location;

public sealed class AppleLocationTracker : ILocationTracker, IAsyncDisposable
{
    private readonly AppUIHub _hub;
    private readonly MutableState<GeoPoint?> _lastKnown;
    private CLLocationManager? _manager;

    public IState<GeoPoint?> LastKnown => _lastKnown;
    public bool IsTracking { get; private set; }

    public AppleLocationTracker(AppUIHub hub)
    {
        _hub = hub;
        _lastKnown = hub.StateFactory.NewMutable(
            (GeoPoint?)null,
            StateCategories.Get(GetType(), nameof(LastKnown)));
    }

    public async Task Start(CancellationToken cancellationToken)
    {
        if (IsTracking)
            return;

        IsTracking = true;
        var settings = await _hub.LocalSettings.LocalAppSettings().Get(cancellationToken).ConfigureAwait(false);
        var accuracy = settings.LocationAccuracyOrDefault;
        MainThread.BeginInvokeOnMainThread(() => {
            _manager ??= CreateManager();
            _manager.SetAccuracy(accuracy);
            _manager.RequestWhenInUseAuthorization();
            _manager.StartUpdatingLocation();
        });
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

    public async ValueTask DisposeAsync()
    {
        IsTracking = false;
        var manager = Interlocked.Exchange(ref _manager, null);
        if (manager is null)
            return;

        // TODO: check if mainthread is really required
        await MainThread.InvokeOnMainThreadAsync(() => {
                manager.LocationsUpdated -= OnLocationsUpdated;
                manager.StopUpdatingLocation();
                manager.DisposeSilently();
            })
            .ConfigureAwait(false);
    }

    // Private methods

    private CLLocationManager CreateManager()
    {
        var manager = new CLLocationManager {
            AllowsBackgroundLocationUpdates = true,
            PausesLocationUpdatesAutomatically = true,
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
        var coordinate = location.Coordinate;
        _lastKnown.Value = new GeoPoint(coordinate.Latitude, coordinate.Longitude, accuracy, bearing);
    }
}
