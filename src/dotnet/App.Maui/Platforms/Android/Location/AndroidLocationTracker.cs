using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor.App.Services;
using Android.Content;
using Android.Hardware;
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
    private readonly HeadingListener _headingListener;
    private LocationManager? _locationManager;
    private SensorManager? _sensorManager;

    private static Context Context => Platform.AppContext;

    public AndroidLocationTracker(AppUIHub hub) : base(hub)
    {
        _listener = new Listener(this);
        _headingListener = new HeadingListener(this);
    }

    public void Dispose()
    {
        IsTracking = false;
        BeginDispatchToMainThread(StopLocationUpdates);
        BeginDispatchToMainThread(StopHeadingSensor);
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

    // Protected/internal methods

    protected override void StartHeadingUpdates()
        => BeginDispatchToMainThread(StartHeadingSensor);

    protected override void StopHeadingUpdates()
        => BeginDispatchToMainThread(StopHeadingSensor);

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

    private void StartHeadingSensor()
    {
        _sensorManager ??= (SensorManager?)Context.GetSystemService(Android.Content.Context.SensorService);
        var sensor = _sensorManager?.GetDefaultSensor(SensorType.RotationVector);
        if (sensor is null) {
            Log.LogWarning("StartHeadingSensor: no rotation vector sensor");
            return;
        }

        _sensorManager!.RegisterListener(_headingListener, sensor, SensorDelay.Ui);
    }

    private void StopHeadingSensor()
        => _sensorManager?.UnregisterListener(_headingListener);

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

    private sealed class HeadingListener(AndroidLocationTracker tracker) : Java.Lang.Object, ISensorEventListener
    {
        private readonly float[] _rotationMatrix = new float[9];
        private readonly float[] _orientation = new float[3];
        private GeoPoint? _declinationPoint;
        private float _declination;

        public void OnSensorChanged(SensorEvent? e)
        {
            if (e?.Values is not { Count: >= 3 } values)
                return;

            SensorManager.GetRotationMatrixFromVector(_rotationMatrix, values.ToArray());
            SensorManager.GetOrientation(_rotationMatrix, _orientation);
            var magneticHeading = _orientation[0] * 180 / MathF.PI;
            tracker.SetHeading(magneticHeading + GetDeclination() + GetScreenRotation());
        }

        public void OnAccuracyChanged(Sensor? sensor, [GeneratedEnum] SensorStatus accuracy) { }

        private float GetDeclination()
        {
            // The rotation vector points at magnetic north; the declination turns that into true north.
            if (tracker.CachedFix?.Point is not { } point)
                return 0;
            if (ReferenceEquals(point, _declinationPoint))
                return _declination;

            using var field = new GeomagneticField(
                (float)point.Latitude, (float)point.Longitude, 0, Java.Lang.JavaSystem.CurrentTimeMillis());
            _declinationPoint = point;
            _declination = field.Declination;
            return _declination;
        }
    }
}
