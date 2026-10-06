namespace ActualChat.WebHooks;

[DataContract, MessagePackObject]
public readonly partial record struct WebHookScopeRef(
    [property: DataMember(Order = 0), Key(0)] WebHookScope Scope,
    [property: DataMember(Order = 1), Key(1)] string Id
    ) : IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => WebHookScopeIds.ToShardKey(Scope, Id);
}
