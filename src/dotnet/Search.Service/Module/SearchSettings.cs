namespace ActualChat.Search.Module;

public sealed class SearchSettings
{
    // DBs
    public string Db { get; set; } = "";
    public string Redis { get; set; } = "";
    public bool IsSearchEnabled { get; set; }
    public string ElasticCloudId { get; set; } = "";
    public string ElasticApiKey { get; set; } = "";

    public bool IsCloudElastic => !ElasticCloudId.IsNullOrEmpty() && !ElasticApiKey.IsNullOrEmpty();
}
