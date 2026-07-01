using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor.App.Services;
using CoreLocation;

namespace ActualChat.App.Maui.Location;

public sealed class AppleLocationTracker(AppUIHub hub) : MauiLocationTrackerBase(hub), IAsyncDisposable
{
    private CLLocationManager? _manager;

    public override async Task Start(CancellationToken cancellationToken)
    {
        if (IsTracking)
            return;

        IsTracking = true;
        var accuracy = await GetAccuracy(cancellationToken).ConfigureAwait(false);
        MainThread.BeginInvokeOnMainThread(() => {
            _manager ??= CreateManager();
            _manager.SetAccuracy(accuracy);
            _manager.RequestWhenInUseAuthorization();
            _manager.StartUpdatingLocation();
        });
    }

    public override Task Stop(CancellationToken cancellationToken)
    {
        if (!IsTracking)
            return Task.CompletedTask;

        IsTracking = false;
        SetLocation(null);
        MainThread.BeginInvokeOnMainThread(() => _manager?.StopUpdatingLocation());
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        IsTracking = false;
        var manager = Interlocked.Exchange(ref _manager, null);
        if (manager is null)
            return;

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
        if (e.Locations.LastOrDefault() is { } location)
            SetLocation(location.ToGeoPoint());
    }
}
