namespace ActualChat.Users;

/// <summary>
/// Represents a time zone with Windows and IANA identifiers.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record TimeZone(
    [property: DataMember, Key(0)] string Id) : IHasId<string>
{
    [DataMember, Key(1)] public string IanaName { get; set; } = "";
}
