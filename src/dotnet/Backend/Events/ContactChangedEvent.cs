using ActualChat.Contacts;

namespace ActualChat;

[DataContract, MessagePackObject(true)]
public partial record ContactChangedEvent(
    [property: DataMember] Contact Contact,
    [property: DataMember] Contact? OldContact,
    [property: DataMember] ChangeKind ChangeKind
) : EventCommand, IHasShardKey<ContactId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ContactId ShardKey => Contact.Id;
}
