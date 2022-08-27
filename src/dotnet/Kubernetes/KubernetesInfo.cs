namespace ActualChat.Kubernetes;

public static class KubernetesInfo
{
    private static string? _podIp;

    // ReSharper disable once InconsistentNaming
    public static string? POD_IP => _podIp = Environment.GetEnvironmentVariable("POD_IP");
}
