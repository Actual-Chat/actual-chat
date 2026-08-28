namespace ActualChat.Users;

/// <summary>
/// Represents a time zone with Windows and IANA identifiers.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record TimeZone(
    [property: DataMember, Key(0)] string Id) : IHasId<string>
{
    [DataMember, Key(1)] public string IanaName { get; set; } = "";
    [DataMember, Key(2)] public int UtcOffsetMinutes { get; set; }
    [DataMember, Key(3)] public string City { get; set; } = "";
    [DataMember, Key(4)] public string CountryCode { get; set; } = "";
    [DataMember, Key(5)] public string CountryName { get; set; } = "";

    public string GetUtcOffsetText()
        => $"UTC{(UtcOffsetMinutes < 0 ? '-' : '+')}{Math.Abs(UtcOffsetMinutes) / 60:D2}:{Math.Abs(UtcOffsetMinutes) % 60:D2}";
}
