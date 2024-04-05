
namespace ActualChat.MLSearch.Module;

public sealed class MLSearchSettings
{
    public bool IsEnabled { get; set; }
    public bool IsInitialIndexingDisabled { get; set; }
    public string Db { get; set; } = "";
    public string Redis { get; set; } = "";
    public string OpenSearchDb { get; set; } = "";

    public string? OpenSearchClusterUri { get; set; }
    public string? OpenSearchModelGroup { get; set; }
}
