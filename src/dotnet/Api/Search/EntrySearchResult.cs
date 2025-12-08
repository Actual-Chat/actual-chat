using MemoryPack;

namespace ActualChat.Search;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class EntrySearchResult : SearchResult
{
    [DataMember, MemoryPackOrder(2)]
    public ApiSet<string> HighlightedWords { get; init; } = [];

    [IgnoreDataMember, MemoryPackIgnore]
    public TextEntryId EntryId => field ??= TextEntryId.Parse(Id);

    public EntrySearchResult(TextEntryId id, SearchMatch searchMatch)
        : base(id.Value, searchMatch)
    { }

    [MemoryPackConstructor]
    private EntrySearchResult(string id, SearchMatch searchMatch)
        : base(id, searchMatch)
    { }
}
