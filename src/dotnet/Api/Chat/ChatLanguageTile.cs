namespace ActualChat.Chat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial class ChatLanguageTile
{
    [DataMember, MemoryPackOrder(0), Key(0)] public Range<long> LidTileRange { get; init; }
    // Entries area always sorted by Id!
    [DataMember, MemoryPackOrder(1), Key(1)] public ChatEntryLanguage[] Entries { get; init; } = [];

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public bool IsEmpty => Entries.Length == 0;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor, SerializationConstructor]
    public ChatLanguageTile() { }

    public ChatLanguageTile(Range<long> lidTileRange, ChatEntryLanguage[] entries)
    {
        LidTileRange = lidTileRange;
        Entries = entries;
    }

    public ChatLanguageTile(IEnumerable<ChatLanguageTile> tiles)
    {
        var entries = new List<ChatEntryLanguage>();
        var lidTile = new Range<long>(long.MaxValue, long.MinValue);
        foreach (var tile in tiles) {
            lidTile = lidTile.MinMaxWith(tile.LidTileRange);
            foreach (var entry in tile.Entries)
                entries.Add(entry);
        }

        LidTileRange = lidTile;
        Entries = entries.ToArray();
    }
}
