using CoreLocation;

namespace ActualChat.App.Maui.Location;

public static class CLLocationManagerExt
{
    public static void SetAccuracy(this CLLocationManager manager, GeoTrackingAccuracy accuracy)
    {
        var (desiredAccuracy, distanceFilter) = accuracy switch {
            GeoTrackingAccuracy.High => (CLLocation.AccuracyBest, 10d),
            GeoTrackingAccuracy.Low => (CLLocation.AccuracyHundredMeters, 100d),
            _ => (CLLocation.AccuracyNearestTenMeters, 50d),
        };
        manager.DesiredAccuracy = desiredAccuracy;
        manager.DistanceFilter = distanceFilter;
    }
}
