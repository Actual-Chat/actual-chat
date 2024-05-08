namespace ActualChat.MLSearch.Engine.OpenSearch.Configuration;

internal class OpenSearchNamingPolicy(JsonNamingPolicy policy)
{
    public string ConvertName(string name) => policy.ConvertName(name);
}
