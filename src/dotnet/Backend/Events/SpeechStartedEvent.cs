namespace ActualChat;

/// <summary>
/// Fired when an author starts streaming live audio into a chat —
/// per utterance, before any transcript or chat entry exists.
/// </summary>
[DataContract, MessagePackObject(true)]
public partial record SpeechStartedEvent(
    [property: DataMember] ChatId ChatId,
    [property: DataMember] AuthorId AuthorId,
    [property: DataMember] Moment StartedAt
) : EventCommand, IHasShardKey<ChatId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ChatId ShardKey => ChatId;
}
