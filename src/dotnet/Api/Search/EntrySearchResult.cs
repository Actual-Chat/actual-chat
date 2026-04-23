namespace ActualChat.Search;

/// <summary>
/// Represents a chat entry match from a search query.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class EntrySearchResult : SearchResult
{
    [DataMember, MemoryPackOrder(2)]
    public ApiSet<string> HighlightedWords { get; init; } = [];

    [IgnoreDataMember, MemoryPackIgnore]
    public ChatEntryId EntryId => field ??= ChatEntryId.Parse(Id);

    public EntrySearchResult(ChatEntryId id, SearchMatch searchMatch)
        : base(id.Value, searchMatch)
    { }

    [ConstructorShape, MemoryPackConstructor]
    private EntrySearchResult(string id, SearchMatch searchMatch)
        : base(id, searchMatch)
    { }
}
