namespace ActualChat;

/// <summary>
/// A geographic position: WGS84 latitude/longitude with optional horizontal
/// <see cref="Accuracy"/> (meters) and movement <see cref="Bearing"/> (degrees).
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record GeoPoint(
    [property: DataMember, MemoryPackOrder(0), Key(0)] double Latitude,
    [property: DataMember, MemoryPackOrder(1), Key(1)] double Longitude,
    [property: DataMember, MemoryPackOrder(2), Key(2)] float? Accuracy = null,
    [property: DataMember, MemoryPackOrder(3), Key(3)] float? Bearing = null
)
{
    public double DistanceTo(GeoPoint other)
    {
        // Haversine great-circle distance in meters.
        const double earthRadius = 6_371_000;
        var lat1 = Latitude * Math.PI / 180;
        var lat2 = other.Latitude * Math.PI / 180;
        var dLat = lat2 - lat1;
        var dLon = (other.Longitude - Longitude) * Math.PI / 180;
        var h = (Math.Sin(dLat / 2) * Math.Sin(dLat / 2))
            + (Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2));
        return 2 * earthRadius * Math.Asin(Math.Sqrt(h));
    }
}
