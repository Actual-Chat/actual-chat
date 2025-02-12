using ActualChat.Contacts;
using MemoryPack;

namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record ContactChangedEvent(
    [property: DataMember, MemoryPackOrder(1)] Contact Contact,
    [property: DataMember, MemoryPackOrder(2)] Contact? OldContact,
    [property: DataMember, MemoryPackOrder(3)] ChangeKind ChangeKind
) : EventCommand, IHasShardKey<ContactId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public ContactId ShardKey => Contact.Id;
}
