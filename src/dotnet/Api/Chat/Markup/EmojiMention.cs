namespace ActualChat.Chat;

[DataContract, MessagePackObject]
public sealed class EmojiMention(MentionRef id, string name = "") : MentionMarkup(id, name)
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public EmojiRef EmojiRef => (EmojiRef)Id.Target;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public string? Glyph { get; init; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public Picture? CustomPicture { get; init; }
}
