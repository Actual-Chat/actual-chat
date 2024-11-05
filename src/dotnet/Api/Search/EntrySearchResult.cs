using MemoryPack;

namespace ActualChat.Search;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class EntrySearchResult : SearchResult
{
    [IgnoreDataMember, MemoryPackIgnore] public TextEntryId EntryId => new (Id);
    [DataMember, MemoryPackOrder(2)]  public ApiSet<string> HighlightedWords { get; init; } = [];

    [MemoryPackConstructor]
    public EntrySearchResult(string id, SearchMatch searchMatch) : base(id, searchMatch)
    { }

    public EntrySearchResult(TextEntryId id, SearchMatch searchMatch) : base(id, searchMatch)
    { }
}
