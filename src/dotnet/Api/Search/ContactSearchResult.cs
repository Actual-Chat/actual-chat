using MemoryPack;

namespace ActualChat.Search;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class ContactSearchResult : SearchResult
{
    [IgnoreDataMember, MemoryPackIgnore]
    [field: AllowNull, MaybeNull]
    public ContactId ContactId => field ??= ContactId.Parse(Id);

    public ContactSearchResult(ContactId id, SearchMatch searchMatch)
        : base(id.Value, searchMatch)
    { }

    [MemoryPackConstructor]
    private ContactSearchResult(string id, SearchMatch searchMatch)
        : base(id, searchMatch)
    { }
}
