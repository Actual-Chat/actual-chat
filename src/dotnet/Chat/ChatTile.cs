using MemoryPack;

namespace ActualChat.Chat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class ChatTile
{
    [DataMember, MemoryPackOrder(0)] public Range<long> IdTileRange { get; init; }
    [DataMember, MemoryPackOrder(1)] public bool IncludesRemoved { get; init; }
    [DataMember, MemoryPackOrder(2)] public Range<Moment> BeginsAtRange { get; init; }
    [DataMember, MemoryPackOrder(3)] public ApiArray<ChatEntry> Entries { get; init; } = ApiArray<ChatEntry>.Empty; // Always sorted by Id!

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsEmpty => Entries.Count == 0;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
    public ChatTile() { }

    public ChatTile(Range<long> idTileRange, bool includesRemoved, ApiArray<ChatEntry> entries)
    {
        var beginsAtRange = new Range<Moment>(Moment.MaxValue, Moment.MinValue);
        foreach (var entry in entries)
            beginsAtRange = beginsAtRange.MinMaxWith(entry.BeginsAt);

        IdTileRange = idTileRange;
        IncludesRemoved = includesRemoved;
        BeginsAtRange = (beginsAtRange.Start, beginsAtRange.End + TimeSpan.FromTicks(1));
        Entries = entries;
    }

    public ChatTile(IEnumerable<ChatTile> tiles, bool includesRemoved)
    {
        var entries = new List<ChatEntry>();
        var idTile = new Range<long>(long.MaxValue, long.MinValue);
        var beginsAtRange = new Range<Moment>(Moment.MaxValue, Moment.MinValue);
        foreach (var tile in tiles) {
            idTile = idTile.MinMaxWith(tile.IdTileRange);
            beginsAtRange = beginsAtRange.MinMaxWith(tile.BeginsAtRange);
            foreach (var entry in tile.Entries)
                entries.Add(entry);
        }

        IdTileRange = idTile;
        IncludesRemoved = includesRemoved;
        BeginsAtRange = (beginsAtRange.Start, beginsAtRange.End + TimeSpan.FromTicks(1));
        Entries = entries.ToApiArray();
    }
}
