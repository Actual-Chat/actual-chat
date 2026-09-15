namespace ActualChat.MLSearch.Documents;

internal interface IHasRoutingKey<in TId>
    where TId : ObjectId
{
    static virtual string GetRoutingKey(TId id) => id.Value;
}
