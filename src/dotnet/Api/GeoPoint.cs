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
    public string ToDisplayText()
        => $"{Latitude:0.######}, {Longitude:0.######}";

    public string ToOpenStreetMapUrl()
        => $"https://www.openstreetmap.org/?mlat={Latitude:0.######}&mlon={Longitude:0.######}"
            + $"#map=15/{Latitude:0.######}/{Longitude:0.######}";
}
