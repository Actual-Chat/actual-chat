namespace ActualChat.MLSearch.Engine.OpenSearch.Setup;

internal sealed class EmbeddingModelProps(string id, int embeddingDimension, string uniqueKey)
{
    public string Id => id;
    public int EmbeddingDimension => embeddingDimension;
    public string UniqueKey => uniqueKey;
}
