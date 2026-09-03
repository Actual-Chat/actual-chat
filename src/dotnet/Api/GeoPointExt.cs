namespace ActualChat;

public static class GeoPointExt
{
    private const string CoordinateFormat = "0.######";

    extension(GeoPoint point)
    {
        public string ToCoordinatesText()
            => $"{point.LatitudeText()},{point.LongitudeText()}";

        public string LatitudeText()
            => point.Latitude.ToString(CoordinateFormat, null);

        public string LongitudeText()
            => point.Longitude.ToString(CoordinateFormat, null);

        public double DistanceTo(GeoPoint other)
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
    }
}
