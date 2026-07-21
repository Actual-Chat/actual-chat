namespace ActualChat;

public static class GeoPointExt
{
    public static double DistanceTo(this GeoPoint point, GeoPoint other)
    {
        // Haversine great-circle distance in meters.
        const double earthRadius = 6_371_000;
        var lat1 = point.Latitude * Math.PI / 180;
        var lat2 = other.Latitude * Math.PI / 180;
        var dLat = lat2 - lat1;
        var dLon = (other.Longitude - point.Longitude) * Math.PI / 180;
        var h = (Math.Sin(dLat / 2) * Math.Sin(dLat / 2))
            + (Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2));
        return 2 * earthRadius * Math.Asin(Math.Sqrt(h));
    }

    public static string DistanceTextTo(this GeoPoint point, GeoPoint other)
    {
        var meters = point.DistanceTo(other);
        return meters switch {
            < 1 => "1 m",
            < 1000 => $"{meters:F0} m",
            < 10_000 => $"{meters / 1000:F1} km",
            _ => $"{meters / 1000:F0} km",
        };
    }
}
