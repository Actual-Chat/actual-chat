namespace ActualChat;

/// <summary>
/// A geographic position: WGS84 latitude/longitude with optional horizontal
/// <see cref="Accuracy"/> (meters) and <see cref="Bearing"/> (degrees clockwise from north): the compass
/// heading where the device reads it, otherwise the GPS course, which exists only while moving.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record GeoPoint(
    [property: DataMember, Key(0)] double Latitude,
    [property: DataMember, Key(1)] double Longitude,
    [property: DataMember, Key(2)] float? Accuracy = null,
    [property: DataMember, Key(3)] float? Bearing = null
)
{
    public string ToDisplayText()
        => $"{Latitude:0.######}, {Longitude:0.######}";

    public string ToOpenStreetMapUrl()
        => $"https://www.openstreetmap.org/?mlat={Latitude:0.######}&mlon={Longitude:0.######}"
            + $"#map=15/{Latitude:0.######}/{Longitude:0.######}";

    public string ToGoogleMapsUrl()
        => $"https://www.google.com/maps?q={Latitude:0.######},{Longitude:0.######}";
}
