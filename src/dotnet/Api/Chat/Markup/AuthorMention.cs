namespace ActualChat.Chat;

[DataContract, MessagePackObject]
public sealed class AuthorMention(MentionRef id, string name = "") : MentionMarkup(id, name)
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public AuthorId AuthorId => (AuthorId)Id.Target;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public Author? Author { get; init; }
}
