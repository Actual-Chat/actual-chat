namespace ActualChat.Chat;

/// <summary>
/// A tile of <see cref="ChatContentItem"/>s for a chat, covering an entry-LocalId range
/// and filtered by a <see cref="ChatContentKind"/> mask. Used as the data unit of the
/// right-panel content lists.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial class ChatContentTile
{
    [DataMember, Key(0)] public Range<long> EntryLidTileRange { get; init; }
    [DataMember, Key(1)] public ChatContentKind KindMask { get; init; }
    // Items are always sorted by (EntryLocalId, Kind, LocalIndex)
    [DataMember, Key(2)] public ChatContentItem[] Items { get; init; } = [];

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsEmpty => Items.Length == 0;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, SerializationConstructor]
    public ChatContentTile() { }

    public ChatContentTile(Range<long> entryLidTileRange, ChatContentKind kindMask, ChatContentItem[] items)
    {
        EntryLidTileRange = entryLidTileRange;
        KindMask = kindMask;
        Items = items;
    }
}
