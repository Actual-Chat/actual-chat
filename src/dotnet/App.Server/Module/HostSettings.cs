using ActualChat.Hosting;

namespace ActualChat.App.Server.Module;

public class HostSettings
{
    public AppKind? AppKind { get; set; }
    public string BaseUrl { get; set; } = "";
    public string WebRootPath { get; set; } = "";
    public bool AssumeHttps { get; set; } = false;

    /// <summary>
    /// Path to the folder or Google cloud storage bucket <br/>(example: <c>gs://BUCKET_NAME/OBJECT_NAME</c>)
    /// </summary>
    public string DataProtection { get; set; } = "";
    public string OpenTelemetryEndpoint { get; set; } = "";

    // Obsolete

    [Obsolete("Use BaseUrl instead.")]
    public string BaseUri { get => BaseUrl; set => BaseUrl = value; }
}
