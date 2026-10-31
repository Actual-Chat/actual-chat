using ActualChat.UI.Blazor.App.Services;
using CoreLocation;

namespace ActualChat.App.Maui.Location;

public static class CLLocationExt
{
    // NSDate's reference date, i.e. what SecondsSinceReferenceDate counts from
    private static readonly Moment ReferenceDate = new (new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    public static GeoFix ToGeoFix(this CLLocation location)
    {
        // Via SecondsSinceReferenceDate rather than the NSDate -> DateTime cast: no DateTimeKind to get wrong.
        var at = ReferenceDate + TimeSpan.FromSeconds(location.Timestamp.SecondsSinceReferenceDate);
        return new GeoFix(location.ToGeoPoint(), at);
    }

    public static GeoPoint ToGeoPoint(this CLLocation location)
    {
        var accuracy = location.HorizontalAccuracy >= 0 ? (float)location.HorizontalAccuracy : (float?)null;
        var bearing = location.Course >= 0 ? (float)location.Course : (float?)null;
        var coordinate = location.Coordinate;
        return new GeoPoint(coordinate.Latitude, coordinate.Longitude, accuracy, bearing);
    }
}
