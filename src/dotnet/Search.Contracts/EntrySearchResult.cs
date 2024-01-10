using MemoryPack;

namespace ActualChat.Search;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class EntrySearchResult : SearchResult
{
    [IgnoreDataMember, MemoryPackIgnore] public TextEntryId EntryId => new (Id);

    [MemoryPackConstructor]
    public EntrySearchResult(string id, SearchMatch searchMatch) : base(id, searchMatch)
    { }

    public EntrySearchResult(TextEntryId id, SearchMatch searchMatch) : base(id, searchMatch)
    { }
}
