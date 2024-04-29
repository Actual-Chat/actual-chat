namespace ActualChat.MLSearch.Engine.OpenSearch;

internal class OpenSearchNamingPolicy(JsonNamingPolicy policy)
{
    public string ConvertName(string name) => policy.ConvertName(name);
}
