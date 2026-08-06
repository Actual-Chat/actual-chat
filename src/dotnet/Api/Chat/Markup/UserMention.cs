
namespace ActualChat.Chat;

[DataContract, MessagePackObject]
public sealed class UserMention(MentionRef id, string name = "") : MentionMarkup(id, name)
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public UserId UserId => (UserId)Id.Target;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public Account? Account { get; init; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsChatMember { get; init; }
}
