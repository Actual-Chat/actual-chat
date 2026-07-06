using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor.App.Services;
using CoreLocation;
using Foundation;

namespace ActualChat.App.Maui.Location;

public sealed class AppleLocationTracker(AppUIHub hub) : MauiLocationTrackerBase(hub), IAsyncDisposable
{
    private CLLocationManager? _manager;

    public override async Task Start(CancellationToken cancellationToken)
    {
        if (IsTracking)
            return;

        IsTracking = true;
        SetError(null);
        try {
            var accuracy = await GetAccuracy(cancellationToken).ConfigureAwait(false);
            MainThread.BeginInvokeOnMainThread(() => {
                _manager ??= CreateManager();
                _manager.SetAccuracy(accuracy);
                _manager.RequestWhenInUseAuthorization();
                _manager.StartUpdatingLocation();
            });
        }
        catch (Exception e) {
            // Roll back so a later Start can retry from a clean state.
            IsTracking = false;
            SetError(ToTrackingError(e));
            throw;
        }
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
                manager.Failed -= OnFailed;
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
        manager.Failed += OnFailed;
        return manager;
    }

    private void OnLocationsUpdated(object? sender, CLLocationsUpdatedEventArgs e)
    {
        if (e.Locations.LastOrDefault() is { } location)
            SetLocation(location.ToGeoPoint());
    }

    private void OnFailed(object? sender, NSErrorEventArgs e)
    {
        Log.LogError(e.Error.ToException(), "Failed to track location");
        var code = e.Error is { } error ? (CLError)error.Code : CLError.LocationUnknown;
        SetError(code switch {
            CLError.Denied or CLError.RegionMonitoringDenied or CLError.PromptDeclined
                => GeoTrackingError.PermissionDenied,
            CLError.LocationUnknown or CLError.Network or CLError.HeadingFailure
                or CLError.RegionMonitoringFailure or CLError.RegionMonitoringSetupDelayed
                or CLError.RegionMonitoringResponseDelayed or CLError.GeocodeFoundNoResult
                or CLError.GeocodeFoundPartialResult or CLError.GeocodeCanceled
                or CLError.DeferredFailed or CLError.DeferredNotUpdatingLocation
                or CLError.DeferredAccuracyTooLow or CLError.DeferredDistanceFiltered
                or CLError.DeferredCanceled or CLError.RangingFailure
                or CLError.RangingUnavailable or CLError.HistoricalLocationError
                => GeoTrackingError.PositionUnavailable,
            _ => throw new ArgumentOutOfRangeException(nameof(code), code, null),
        });
    }
}
