using CoreLocation;

namespace ActualChat.App.Maui.Location;

public static class CLLocationExt
{
    public static GeoPoint ToGeoPoint(this CLLocation location)
    {
        var accuracy = location.HorizontalAccuracy >= 0 ? (float)location.HorizontalAccuracy : (float?)null;
        var bearing = location.Course >= 0 ? (float)location.Course : (float?)null;
        var coordinate = location.Coordinate;
        return new GeoPoint(coordinate.Latitude, coordinate.Longitude, accuracy, bearing);
    }
}
