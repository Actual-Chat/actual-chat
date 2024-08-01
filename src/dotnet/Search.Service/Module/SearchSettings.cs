namespace ActualChat.Search.Module;

public sealed class SearchSettings
{
    public bool IsSearchEnabled { get; set; }
    public string ClientCertificatePath { get; set; } = "";
    public string LocalUri { get; set; } = "http://localhost:9200";
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan ContactIndexingDelay { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan ContactIndexingSignalInterval { get; set; } = TimeSpan.FromSeconds(1);
}
