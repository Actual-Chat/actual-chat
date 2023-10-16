using ActualChat.Hosting;

namespace ActualChat.App.Server.Module;

public sealed class HostSettings
{
    public AppKind? AppKind { get; set; }
    public bool? IsTested { get; set; }

    // Please don't rename this - we use externally stored settings / env variables to fulfill the value
    public string BaseUri { get; set; } = "";
    public string WebRootPath { get; set; } = "";
    public bool AssumeHttps { get; set; } = false;

    /// <summary>
    /// Path to the folder or Google cloud storage bucket <br/>(example: <c>gs://BUCKET_NAME/OBJECT_NAME</c>)
    /// </summary>
    public string DataProtection { get; set; } = "";
    public string OpenTelemetryEndpoint { get; set; } = "";

    public int? LivelinessCpuLimit { get; set; }
    public int? ReadinessCpuLimit { get; set; }
}
