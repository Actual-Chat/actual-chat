namespace ActualChat;

// A voice entry has just begun to stream: its text is empty until it settles, so nothing else announces it.
// Raised only where a handler needs it (see ChatsBackend).
[DataContract, MessagePackObject(true)]
public partial record ChatEntryStreamingStartedEvent(
    [property: DataMember] ChatEntry Entry,
    [property: DataMember] AuthorFull Author
) : EventCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => Entry.ChatId.ShardKey;
}
