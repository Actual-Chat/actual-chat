
namespace ActualChat.Search;

/// <summary>
/// Represents a chat entry match from a search query.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public partial class EntrySearchResult : SearchResult
{
    [DataMember, MemoryPackOrder(2)]
    public ApiSet<string> HighlightedWords { get; init; } = [];

    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public TextEntryId EntryId => field ??= TextEntryId.Parse(Id);

    public EntrySearchResult(TextEntryId id, SearchMatch searchMatch)
        : base(id.Value, searchMatch)
    { }

    [MemoryPackConstructor, SerializationConstructor]
    private EntrySearchResult(string id, SearchMatch searchMatch)
        : base(id, searchMatch)
    { }
}
