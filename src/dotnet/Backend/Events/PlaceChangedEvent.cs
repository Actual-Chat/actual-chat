namespace ActualChat;

[DataContract, MessagePackObject(true)]
public partial record PlaceChangedEvent(
    [property: DataMember] Place Place,
    [property: DataMember] Place? OldPlace,
    [property: DataMember] ChangeKind ChangeKind
) : EventCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => Place.Id.ShardKey;
}
