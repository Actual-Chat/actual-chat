namespace ActualChat.Contacts;

[DataContract, MessagePackObject]
public sealed partial record ChatIdRange(
    [property: DataMember, Key(0)] ChatId ChatId,
    [property: DataMember, Key(1)] Range<long> IdRange)
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public long LastEntryLid => IdRange.End - 1;
}
