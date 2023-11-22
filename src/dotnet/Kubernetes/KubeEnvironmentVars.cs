namespace ActualChat.Kubernetes;

internal static class KubeEnvironmentVars
{
    private static string? _podIP;
    private static string? _kubernetesServiceHost;
    private static int? _kubernetesServicePort;

    public static string PodIP
        => _podIP ??= Environment.GetEnvironmentVariable("POD_IP") ?? "";
    public static string KubernetesServiceHost
        => _kubernetesServiceHost ??= Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST") ?? "";
    public static int KubernetesServicePort {
        get {
            var sPort = Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_PORT") ?? "";
            return _kubernetesServicePort ??= NumberExt.TryParsePositiveInt(sPort, out var port) ? port : 0;
        }
    }
}
