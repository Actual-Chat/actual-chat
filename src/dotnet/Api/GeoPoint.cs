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
);
