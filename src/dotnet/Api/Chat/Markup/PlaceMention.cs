namespace ActualChat.Chat;

[DataContract, MessagePackObject]
public sealed class PlaceMention(MentionRef id, string name = "") : MentionMarkup(id, name)
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public PlaceId PlaceId => (PlaceId)Id.Target;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public Place? Place { get; init; }
}
