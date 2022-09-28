namespace ActualChat.App.Server.Module;

public class HostSettings
{
    public string BaseUri { get; set; } = "";
    public string WebRootPath { get; set; } = "";
    public bool AssumeHttps { get; set; } = false;

    /// <summary>
    /// Path to the folder or Google cloud storage bucket <br/>(example: <c>gs://BUCKET_NAME/OBJECT_NAME</c>)
    /// </summary>
    public string DataProtection { get; set; } = "";
    public string OpenTelemetryEndpoint { get; set; } = "";
}
