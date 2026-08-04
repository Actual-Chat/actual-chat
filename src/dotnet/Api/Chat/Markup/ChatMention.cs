namespace ActualChat.Chat;

[DataContract, MessagePackObject]
public sealed class ChatMention(MentionRef id, string name = "") : MentionMarkup(id, name)
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ChatId ChatId => (ChatId)Id.Target;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public Chat? Chat { get; init; }
}
