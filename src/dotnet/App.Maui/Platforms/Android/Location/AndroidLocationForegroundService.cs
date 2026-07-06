using _Microsoft.Android.Resource.Designer;
using ActualChat.UI.Blazor.App.Services;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Locations;
using Android.OS;
using Android.Runtime;
using AndroidX.Core.App;

namespace ActualChat.App.Maui.Location;

[Service(ForegroundServiceType = ForegroundService.TypeLocation)]
public sealed class AndroidLocationForegroundService : Service, ILocationListener
{
    public static class IntentExtras
    {
        public const string Accuracy = nameof(Accuracy);
    }

    public const string ActionStart = "ACTION_START";
    private const string ChannelId = "location_sharing";
    private const int NotificationId = 3002;
    private LocationManager? _locationManager;
    private ILogger Log { get; } = StaticLog.For<AndroidLocationForegroundService>();

    public override void OnCreate()
    {
        Log.LogDebug("OnCreate");
        base.OnCreate();
        CreateNotificationChannel();
    }

    public override void OnDestroy()
    {
        Log.LogDebug("OnDestroy");
        StopLocationUpdates();
        base.OnDestroy();
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if ((intent?.Action ?? "") != ActionStart)
            return StartCommandResult.NotSticky;

        var accuracy = (GeoTrackingAccuracy)intent!.Extras!
            .GetInt(IntentExtras.Accuracy, (int)GeoTrackingAccuracy.Balanced);
        StartForeground1(BuildNotification());
        StartLocationUpdates(accuracy);
        return StartCommandResult.Sticky;
    }

    public void OnLocationChanged(Android.Locations.Location location)
    {
        var accuracy = location.HasAccuracy ? location.Accuracy : (float?)null;
        var bearing = location.HasBearing ? location.Bearing : (float?)null;
        AndroidLocationTracker.ReportLocation(new GeoPoint(location.Latitude, location.Longitude, accuracy, bearing));
    }

    public void OnProviderDisabled(string provider)
        => AndroidLocationTracker.ReportError(GeoTrackingError.PositionUnavailable);
    public void OnProviderEnabled(string provider) { }
    public void OnStatusChanged(string? provider, [GeneratedEnum] Availability status, Bundle? extras) { }

    // Private methods

    private void StartLocationUpdates(GeoTrackingAccuracy accuracy)
    {
        _locationManager = (LocationManager?)GetSystemService(LocationService);
        if (_locationManager is null) {
            Log.LogWarning("StartLocationUpdates: LocationManager is unavailable");
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
                    LocationManager.GpsProvider, minTimeMs, minDistanceM, this, Looper.MainLooper);
            else if (_locationManager.IsProviderEnabled(LocationManager.NetworkProvider))
                _locationManager.RequestLocationUpdates(
                    LocationManager.NetworkProvider, minTimeMs, minDistanceM, this, Looper.MainLooper);
            else {
                Log.LogError("StartLocationUpdates: no location provider is enabled");
                AndroidLocationTracker.ReportError(GeoTrackingError.PositionUnavailable);
            }
        }
        catch (Java.Lang.SecurityException e) {
            Log.LogError(e, "StartLocationUpdates: location permission is not granted");
            AndroidLocationTracker.ReportError(GeoTrackingError.PermissionDenied);
        }
    }

    private void StopLocationUpdates()
    {
        _locationManager?.RemoveUpdates(this);
        _locationManager = null;
    }

    private void StartForeground1(Android.App.Notification notification)
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
            StartForeground(NotificationId, notification, ForegroundService.TypeLocation);
        else
            StartForeground(NotificationId, notification);
    }

    private Android.App.Notification BuildNotification()
        => new NotificationCompat.Builder(this, ChannelId)
            .SetSmallIcon(ResourceConstant.Drawable.notification_app_icon)!
            .SetContentTitle("Sharing your location")!
            .SetOngoing(true)!
            .Build()!;

    private void CreateNotificationChannel()
    {
        var channel = new NotificationChannel(ChannelId, "Location sharing", NotificationImportance.Low);
        var manager = (NotificationManager)GetSystemService(NotificationService)!;
        manager.CreateNotificationChannel(channel);
    }
}
