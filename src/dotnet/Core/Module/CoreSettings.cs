namespace ActualChat.Module;

public sealed class CoreSettings
{
    public string Instance { get; set; } = "dev";
    public string GoogleStorageBucket { get; set; } = ""; // Set it via env. var
    public string GoogleProjectId { get; set; } = ""; // Set it via env. var
    public string GoogleRegionId { get; set; } = "us-central1";
}
