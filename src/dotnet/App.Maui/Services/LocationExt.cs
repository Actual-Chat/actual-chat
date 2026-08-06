using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.App.Maui.Services;

public static class LocationExt
{
    public static GeoFix ToGeoFix(this Microsoft.Maui.Devices.Sensors.Location location)
        => new (location.ToGeoPoint(), new Moment(location.Timestamp));

    public static GeoPoint ToGeoPoint(this Microsoft.Maui.Devices.Sensors.Location location)
    {
        var accuracy = location.Accuracy is { } a ? (float)a : (float?)null;
        var bearing = location.Course is { } c ? (float)c : (float?)null;
        return new GeoPoint(location.Latitude, location.Longitude, accuracy, bearing);
    }
}
