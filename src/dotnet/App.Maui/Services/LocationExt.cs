namespace ActualChat.App.Maui.Services;

public static class LocationExt
{
    public static GeoPoint ToGeoPoint(this Microsoft.Maui.Devices.Sensors.Location location)
    {
        var accuracy = location.Accuracy is { } a ? (float)a : (float?)null;
        var bearing = location.Course is { } c ? (float)c : (float?)null;
        return new GeoPoint(location.Latitude, location.Longitude, accuracy, bearing);
    }
}
