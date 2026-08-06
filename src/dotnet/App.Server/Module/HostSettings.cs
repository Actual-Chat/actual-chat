
namespace ActualChat.App.Server.Module;

/// <summary>
/// Configuration settings for the application server host.
/// </summary>
public sealed class HostSettings
{
    public HostKind? AppKind { get; set; }
    public string ServerRole { get; set; } = "";
    public bool? IsTested { get; set; }
    public bool IsAspireManaged { get; set; }

    // Please don't rename this - we use externally stored settings / env variables to fulfill the value
    public string BaseUri { get; set; } = "";
    public string WebRootPath { get; set; } = "";
    public bool AssumeHttps { get; set; } = false;
    // CIDR list (";" or ","): peers allowed to set X-Forwarded-*; covers loopback, cluster & GCLB ranges
    public string KnownProxyNetworks { get; set; } =
        "127.0.0.0/8;::1/128;10.0.0.0/8;172.16.0.0/12;192.168.0.0/16;fc00::/7;35.191.0.0/16;130.211.0.0/22";
    public string KnownProxies { get; set; } = ""; // Address list (";" or ",")

    /// <summary>
    /// Path to the folder or Google cloud storage bucket <br/>(example: <c>gs://BUCKET_NAME/OBJECT_NAME</c>)
    /// </summary>
    public string DataProtection { get; set; } = "";
    public string OpenTelemetryEndpoint { get; set; } = "";
    /// <summary>
    /// Trace sampling rate for OpenTelemetry (0.0 = none, 1.0 = all). Default: 0.1 (10%).
    /// </summary>
    public double OpenTelemetryTraceSampleRate { get; set; } = 0.1;

    public int? ReadinessCpuLimit { get; set; }
    public string MeshLockSubspace { get; set; } = ""; // "?" means "make it random"
    public string MeshLockOptionsPreset { get; set; } = "";
    public int MeshLockRenewerThreadCount { get; set; } = 2;
}
