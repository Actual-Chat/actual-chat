namespace ActualChat.Search;

/// <summary>
/// Represents a chat entry match from a search query.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true, AllowPrivate = true)]
[method: MemoryPackConstructor, SerializationConstructor]
public sealed partial class FoundChatEntry(ChatEntryId entryId, SearchMatch match) : IHasSearchMatch
{
    [DataMember, MemoryPackOrder(0)] public ChatEntryId EntryId { get; } = entryId;
    [DataMember, MemoryPackOrder(1)] public SearchMatch Match { get; } = match;
    [DataMember, MemoryPackOrder(2)] public ApiSet<string> HighlightedWords { get; init; } = [];

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public string Text => Match.Text;
}
