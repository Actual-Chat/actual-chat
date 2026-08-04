namespace ActualChat.Search;

/// <summary>
/// Represents a contact match from a search query.
/// </summary>
[DataContract, MessagePackObject(true, AllowPrivate = true)]
[method: SerializationConstructor]
public sealed partial class FoundContact(ContactId contactId, SearchMatch match) : IHasSearchMatch
{
    [DataMember] public ContactId ContactId { get; } = contactId;
    [DataMember] public SearchMatch Match { get; } = match;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public string Text => Match.Text;
}
