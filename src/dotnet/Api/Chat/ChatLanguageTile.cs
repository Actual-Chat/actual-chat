namespace ActualChat.Chat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial class ChatLanguageTile
{
    [DataMember, MemoryPackOrder(0), Key(0)] public Range<long> IdTileRange { get; init; }
    // Entries area always sorted by Id!
    [DataMember, MemoryPackOrder(1), Key(1)] public ChatEntryLanguage[] Entries { get; init; } = [];

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public bool IsEmpty => Entries.Length == 0;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor, SerializationConstructor]
    public ChatLanguageTile() { }

    public ChatLanguageTile(Range<long> idTileRange, ChatEntryLanguage[] entries)
    {
        IdTileRange = idTileRange;
        Entries = entries;
    }

    public ChatLanguageTile(IEnumerable<ChatLanguageTile> tiles)
    {
        var entries = new List<ChatEntryLanguage>();
        var idTile = new Range<long>(long.MaxValue, long.MinValue);
        foreach (var tile in tiles) {
            idTile = idTile.MinMaxWith(tile.IdTileRange);
            foreach (var entry in tile.Entries)
                entries.Add(entry);
        }

        IdTileRange = idTile;
        Entries = entries.ToArray();
    }
}
