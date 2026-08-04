namespace ActualChat.Search;

/// <summary>
/// Represents a chat entry match from a search query.
/// </summary>
[DataContract, MessagePackObject(true, AllowPrivate = true)]
[method: SerializationConstructor]
public sealed partial class FoundChatEntry(ChatEntryId entryId, SearchMatch match) : IHasSearchMatch
{
    [DataMember] public ChatEntryId EntryId { get; } = entryId;
    [DataMember] public SearchMatch Match { get; } = match;
    [DataMember] public ApiSet<string> HighlightedWords { get; init; } = [];

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public string Text => Match.Text;
}
