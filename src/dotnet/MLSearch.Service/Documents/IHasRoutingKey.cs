namespace ActualChat.MLSearch.Documents;

internal interface IHasRoutingKey<in TId> where TId : ISymbolIdentifier
{
    static virtual string GetRoutingKey(TId id) => id.Value;
}
