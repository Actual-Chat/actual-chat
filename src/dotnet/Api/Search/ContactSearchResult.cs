namespace ActualChat.Search;

/// <summary>
/// Represents a contact match from a search query.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public partial class ContactSearchResult : SearchResult
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public ContactId ContactId => field ??= ContactId.Parse(Id);

    public ContactSearchResult(ContactId id, SearchMatch searchMatch)
        : base(id.Value, searchMatch)
    { }

    [MemoryPackConstructor, SerializationConstructor]
    private ContactSearchResult(string id, SearchMatch searchMatch)
        : base(id, searchMatch)
    { }
}
