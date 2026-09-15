namespace ActualChat;

public abstract class ObjectId(string value) : StringIdentifier(value)
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public TypedObjectId TypedId => field ??= new TypedObjectId(this);
}
