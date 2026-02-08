using ActualChat.Contacts;

namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public partial record ContactChangedEvent(
    [property: DataMember, MemoryPackOrder(1)] Contact Contact,
    [property: DataMember, MemoryPackOrder(2)] Contact? OldContact,
    [property: DataMember, MemoryPackOrder(3)] ChangeKind ChangeKind
) : EventCommand, IHasShardKey<ContactId>
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public ContactId ShardKey => Contact.Id;
}
