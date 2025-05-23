namespace ActualChat.MLSearch.Documents;

internal interface IHasRoutingKey<in TId>
    where TId : StringIdentifier
{
    static virtual string? GetRoutingKey(TId? id) => id?.Value;
}
