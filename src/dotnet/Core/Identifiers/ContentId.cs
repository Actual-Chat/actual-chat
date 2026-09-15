namespace ActualChat;

public abstract class ContentId(string value) : StringIdentifier(value)
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ContentRef ContentRef => field ??= new ContentRef(this);
}
