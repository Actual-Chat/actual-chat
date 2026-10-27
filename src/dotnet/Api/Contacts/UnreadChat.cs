namespace ActualChat.Contacts;

[DataContract, MessagePackObject]
public sealed partial record UnreadChat(
    [property: DataMember, Key(0)] ChatId ChatId,
    [property: DataMember, Key(1)] long ReadEntryLid,
    [property: DataMember, Key(2)] long LastEntryLid)
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public long UnreadCount => LastEntryLid - ReadEntryLid;
}
