using MemoryPack;

namespace ActualChat.Search;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class ContactSearchResult : SearchResult
{
    [IgnoreDataMember, MemoryPackIgnore] public ContactId ContactId => new (Id);

    [MemoryPackConstructor]
    public ContactSearchResult(string id, SearchMatch searchMatch) : base(id, searchMatch)
    { }

    public ContactSearchResult(ContactId id, SearchMatch searchMatch) : base(id, searchMatch)
    { }
}
