namespace ActualChat.Search;

/// <summary>
/// Represents a contact match from a search query.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true, AllowPrivate = true)]
[method: MemoryPackConstructor, SerializationConstructor]
public sealed partial class FoundContact(ContactId contactId, SearchMatch match) : IHasSearchMatch
{
    [DataMember, MemoryPackOrder(0)] public ContactId ContactId { get; } = contactId;
    [DataMember, MemoryPackOrder(1)] public SearchMatch Match { get; } = match;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public string Text => Match.Text;
}
