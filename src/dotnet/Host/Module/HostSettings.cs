namespace ActualChat.Host.Module;

public class HostSettings
{
    public string BaseUri { get; set; } = "";
    public string OpenTelemetryEndpoint { get; set; } = "";
    /// <summary>
    /// Path to the folder or google cloud storage bucket <br/>(example: <c>gs://BUCKET_NAME/OBJECT_NAME</c>)
    /// </summary>
    public string DataProtection { get; set; } = "";
    public PrometheusSettings? Prometheus { get; set; }
}

public class PrometheusSettings
{
    public string Endpoint { get; set; } = "/metrics";
    public bool Gc { get; set; }
    public bool Contention { get; set; }
    public bool Jit { get; set; }
    public bool ThreadPool { get; set; }
    public bool Exceptions { get; set; }
}