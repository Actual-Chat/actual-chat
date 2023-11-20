namespace ActualChat.Module;

public sealed class CoreServerSettings
{
    public string GoogleStorageBucket { get; set; } = ""; // Set it via env. var
    public string GoogleProjectId { get; set; } = ""; // Set it via env. var
    public string GoogleRegionId { get; set; } = "us-central1";
}
