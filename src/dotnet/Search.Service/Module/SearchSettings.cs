using Elastic.Clients.Elasticsearch;

namespace ActualChat.Search.Module;

public sealed class SearchSettings
{
    // DBs
    public string Db { get; set; } = "";
    public string Redis { get; set; } = "";
    public bool IsSearchEnabled { get; set; }
    public string ElasticCloudId { get; set; } = "";
    public string ElasticApiKey { get; set; } = "";
    public string ElasticLocalUri { get; set; } = "";
    public TimeSpan ElasticRefreshInterval { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan ContactIndexingDelay { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan ContactIndexingSignalInterval { get; set; } = TimeSpan.FromSeconds(1);

    public bool IsCloudElastic => !ElasticCloudId.IsNullOrEmpty() && !ElasticApiKey.IsNullOrEmpty();
}
