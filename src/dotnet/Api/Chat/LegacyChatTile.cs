namespace ActualChat.Chat;

/// <summary>
/// Wire-frozen v2.7 <see cref="ChatTile"/> shape that carries <see cref="LegacyChatEntry"/> arrays.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class LegacyChatTile
{
    [DataMember, MemoryPackOrder(0)] public Range<long> IdTileRange { get; init; }
    [DataMember, MemoryPackOrder(1)] public bool IncludesRemoved { get; init; }
    [DataMember, MemoryPackOrder(2)] public Range<Moment> BeginsAtRange { get; init; }
    [DataMember, MemoryPackOrder(3)] public LegacyChatEntry[] Entries { get; init; } = [];

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsEmpty => Entries.Length == 0;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
    public LegacyChatTile() { }

    public LegacyChatTile(Range<long> idTileRange, bool includesRemoved, Range<Moment> beginsAtRange, LegacyChatEntry[] entries)
    {
        IdTileRange = idTileRange;
        IncludesRemoved = includesRemoved;
        BeginsAtRange = beginsAtRange;
        Entries = entries;
    }
}
