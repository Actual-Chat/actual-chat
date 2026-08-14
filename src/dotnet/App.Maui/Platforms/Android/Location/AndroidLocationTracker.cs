using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor.App.Services;
using Android.Content;
using Android.Locations;
using Android.OS;
using Android.Runtime;

namespace ActualChat.App.Maui.Location;

/// <summary>
/// Requests fused location fixes in-process; the background-location grant comes from
/// the activities foreground service holding the location type while a share is active.
/// </summary>
public sealed class AndroidLocationTracker : MauiLocationTrackerBase, IDisposable
{
    private readonly Listener _listener;
    private LocationManager? _locationManager;

    private static Context Context => Platform.AppContext;

    public AndroidLocationTracker(AppUIHub hub) : base(hub)
        => _listener = new Listener(this);

    public void Dispose()
    {
        IsTracking = false;
        BeginDispatchToMainThread(StopLocationUpdates);
    }

    public override async Task Start(CancellationToken cancellationToken)
    {
        if (IsTracking)
            return;

        IsTracking = true;
        SetError(null);
        try {
            var accuracy = await GetAccuracy(cancellationToken).ConfigureAwait(false);
            BeginDispatchToMainThread(() => StartLocationUpdates(accuracy));
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
        IsTracking = false;
        SetCached(null);
        // Unconditional, like the FGS-era fix: a fresh instance must be able to stop
        // updates it never started.
        BeginDispatchToMainThread(StopLocationUpdates);
        return Task.CompletedTask;
    }

    // Private methods

    private void StartLocationUpdates(GeoTrackingAccuracy accuracy)
    {
        _locationManager ??= (LocationManager?)Context.GetSystemService(Android.Content.Context.LocationService);
        if (_locationManager is null) {
            Log.LogWarning("StartLocationUpdates: LocationManager is unavailable");
            SetError(GeoTrackingError.PositionUnavailable);
            return;
        }

        var minTimeMs = (long)Constants.Location.UpdatePeriod.TotalMilliseconds;
        var minDistanceM = accuracy switch {
            GeoTrackingAccuracy.High => 10f,
            GeoTrackingAccuracy.Low => 100f,
            _ => 50f,
        };
        try {
            if (_locationManager.IsProviderEnabled(LocationManager.GpsProvider))
                _locationManager.RequestLocationUpdates(
                    LocationManager.GpsProvider, minTimeMs, minDistanceM, _listener, Looper.MainLooper);
            else if (_locationManager.IsProviderEnabled(LocationManager.NetworkProvider))
                _locationManager.RequestLocationUpdates(
                    LocationManager.NetworkProvider, minTimeMs, minDistanceM, _listener, Looper.MainLooper);
            else {
                Log.LogError("StartLocationUpdates: no location provider is enabled");
                SetError(GeoTrackingError.PositionUnavailable);
            }
        }
        catch (Java.Lang.SecurityException e) {
            Log.LogError(e, "StartLocationUpdates: location permission is not granted");
            SetError(GeoTrackingError.PermissionDenied);
        }
    }

    private void StopLocationUpdates()
        => _locationManager?.RemoveUpdates(_listener);

    // Nested types

    private sealed class Listener(AndroidLocationTracker tracker) : Java.Lang.Object, ILocationListener
    {
        public void OnLocationChanged(Android.Locations.Location location)
        {
            var accuracy = location.HasAccuracy ? location.Accuracy : (float?)null;
            var bearing = location.HasBearing ? location.Bearing : (float?)null;
            var point = new GeoPoint(location.Latitude, location.Longitude, accuracy, bearing);
            // Location.Time is the fix's UTC epoch ms
            tracker.SetCached(new GeoFix(point, new Moment(TimeSpan.FromMilliseconds(location.Time))));
        }

        public void OnProviderDisabled(string provider)
            => tracker.SetError(GeoTrackingError.PositionUnavailable);
        public void OnProviderEnabled(string provider) { }
        public void OnStatusChanged(string? provider, [GeneratedEnum] Availability status, Bundle? extras) { }
    }
}
