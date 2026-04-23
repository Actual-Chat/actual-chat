namespace ActualChat.Chat;

/// <summary>
/// Legacy v2.6 ChatTile with LegacyChatEntry for backward compatibility.
/// Remove once all clients are migrated past v2.6.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[Obsolete("2025.03: Legacy v2.6 compat type")]
public sealed partial class LegacyChatTile
{
    [DataMember, MemoryPackOrder(0), Key(0)] public Range<long> IdTileRange { get; init; }
    [DataMember, MemoryPackOrder(1), Key(1)] public bool IncludesRemoved { get; init; }
    [DataMember, MemoryPackOrder(2), Key(2)] public Range<Moment> BeginsAtRange { get; init; }
    [DataMember, MemoryPackOrder(3), Key(3)] public LegacyChatEntry[] Entries { get; init; } = [];

    [ConstructorShape, JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
    public LegacyChatTile() { }

#pragma warning disable CS0618 // Obsolete
    public static LegacyChatTile From(ChatTile tile)
        => new() {
            IdTileRange = tile.IdTileRange,
            IncludesRemoved = tile.IncludesRemoved,
            BeginsAtRange = tile.BeginsAtRange,
            Entries = Array.ConvertAll(tile.Entries, LegacyChatEntry.From),
        };
#pragma warning restore CS0618
}
