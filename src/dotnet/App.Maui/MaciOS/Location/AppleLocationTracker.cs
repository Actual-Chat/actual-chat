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
        SetCached(null);
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
                manager.UpdatedHeading -= OnHeadingUpdated;
                manager.Failed -= OnFailed;
                manager.StopUpdatingLocation();
                manager.StopUpdatingHeading();
                manager.DisposeSilently();
            })
            .ConfigureAwait(false);
    }

    // Protected/internal methods

    protected override Task StartHeadingUpdates()
        => DispatchToMainThread(() => {
            if (!CLLocationManager.HeadingAvailable)
                return;

            _manager ??= CreateManager();
            _manager.StartUpdatingHeading();
        });

    protected override Task StopHeadingUpdates()
        => DispatchToMainThread(() => _manager?.StopUpdatingHeading());

    // Private methods

    private CLLocationManager CreateManager()
    {
        var manager = new CLLocationManager {
            AllowsBackgroundLocationUpdates = true,
            PausesLocationUpdatesAutomatically = true,
            HeadingFilter = MinHeadingChange,
        };
        manager.LocationsUpdated += OnLocationsUpdated;
        manager.UpdatedHeading += OnHeadingUpdated;
        manager.Failed += OnFailed;
        return manager;
    }

    private void OnLocationsUpdated(object? sender, CLLocationsUpdatedEventArgs e)
    {
        if (e.Locations.LastOrDefault() is { } location)
            SetCached(location.ToGeoFix());
    }

    private void OnHeadingUpdated(object? sender, CLHeadingUpdatedEventArgs e)
    {
        // HeadingOrientation stays Portrait, so the heading is the device top's - rotated to the screen's here.
        // TrueHeading is negative until there's a location to correct the magnetic one with.
        var heading = e.NewHeading;
        if (heading.HeadingAccuracy < 0) {
            SetHeading(null);
            return;
        }

        var deviceHeading = heading.TrueHeading >= 0 ? heading.TrueHeading : heading.MagneticHeading;
        SetHeading((float)deviceHeading + GetScreenRotation());
    }

    private void OnFailed(object? sender, NSErrorEventArgs e)
    {
        var code = e.Error is { } error ? (CLError)error.Code : CLError.LocationUnknown;
        if (code == CLError.HeadingFailure) {
            // Magnetic interference: the position is still fine, so only the heading is dropped.
            Log.LogWarning(e.Error.ToException(), "Failed to track heading");
            SetHeading(null);
            return;
        }

        Log.LogError(e.Error.ToException(), "Failed to track location");
        var trackingError = code switch {
            CLError.Denied or CLError.RegionMonitoringDenied or CLError.PromptDeclined
                => GeoTrackingError.PermissionDenied,
            CLError.LocationUnknown or CLError.Network
                or CLError.RegionMonitoringFailure or CLError.RegionMonitoringSetupDelayed
                or CLError.RegionMonitoringResponseDelayed or CLError.GeocodeFoundNoResult
                or CLError.GeocodeFoundPartialResult or CLError.GeocodeCanceled
                or CLError.DeferredFailed or CLError.DeferredNotUpdatingLocation
                or CLError.DeferredAccuracyTooLow or CLError.DeferredDistanceFiltered
                or CLError.DeferredCanceled or CLError.RangingFailure
                or CLError.RangingUnavailable or CLError.HistoricalLocationError
                => GeoTrackingError.PositionUnavailable,
            _ => throw new ArgumentOutOfRangeException(nameof(code), code, null),
        };
        if (trackingError == GeoTrackingError.PermissionDenied) {
            IsTracking = false;
            _manager?.StopUpdatingLocation();
        }
        SetError(trackingError);
    }
}
