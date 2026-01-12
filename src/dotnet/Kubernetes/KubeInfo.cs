using ActualLab.IO;

namespace ActualChat.Kubernetes;

public interface IKubeInfo
{
    static bool HasKube()
    {
        var podIp = Environment.GetEnvironmentVariable("POD_IP");
        return !podIp.IsNullOrEmpty();
    }

    ValueTask<Kube?> GetKube(CancellationToken cancellationToken = default);
    ValueTask<Kube> RequireKube(CancellationToken cancellationToken = default);
}

public sealed class KubeInfo(IServiceProvider services) : IKubeInfo, IAsyncDisposable
{
    public FilePath TokenPath { get; init; } = "/var/run/secrets/kubernetes.io/serviceaccount/token";
    public FilePath CACertPath { get; init; } = "/var/run/secrets/kubernetes.io/serviceaccount/ca.crt";

    private IServiceProvider Services { get; } = services;

    private readonly Lock _lock = new();
    private volatile CachedKube? _cachedInfo;
    private volatile KubeToken? _token;
    private Task? _disposeTask;

    public static KubeInfo GetLocal(IServiceProvider services)
    {
        var currentPodIp = Environment.GetEnvironmentVariable("POD_IP");
        if (!currentPodIp.IsNullOrEmpty())
            throw new InvalidOperationException("This method should not be executed within Kubernetes cluster.");

        // Ensure current context is docker-desktop
        ExecuteKubectlCommand("config use-context docker-desktop");
        // Ensure the default service account has the required permissions for Leases
        SetupKubePermissions();

        Environment.SetEnvironmentVariable("KUBERNETES_SERVICE_HOST", "kubernetes.docker.internal");
        Environment.SetEnvironmentVariable("KUBERNETES_SERVICE_PORT", "6443");
        Environment.SetEnvironmentVariable("POD_IP", "127.0.0.1");
        Environment.SetEnvironmentVariable("POD_NAMESPACE", "default");

        var tempDir = Path.GetTempPath();
        var tokenPath = Path.Combine(tempDir, "kube-token");
        var caPath = Path.Combine(tempDir, "kube-ca.crt");

        // Try to get token and CA from kubectl
        var token = ExecuteKubectlCommand("create token default --duration=24h").Trim();
        if (!string.IsNullOrEmpty(token))
            File.WriteAllText(tokenPath, token);

        var caData = ExecuteKubectlCommand("config view --raw -o jsonpath=\"{.clusters[?(@.name=='docker-desktop')].cluster.certificate-authority-data}\"").Trim();
        if (!string.IsNullOrEmpty(caData))
            File.WriteAllBytes(caPath, Convert.FromBase64String(caData));

        return new KubeInfo(services) {
            TokenPath = tokenPath,
            CACertPath = caPath,
        };
    }

    public ValueTask DisposeAsync()
    {
        lock (_lock)
            _disposeTask ??= _token?.DisposeAsync().AsTask() ?? Task.CompletedTask;
        return _disposeTask.ToValueTask();
    }

    public ValueTask<Kube?> GetKube(CancellationToken cancellationToken = default)
    {
        // Double check locking
        if (_cachedInfo != null) return ValueTaskExt.FromResult(_cachedInfo.Value);
        lock (_lock) {
            ObjectDisposedException.ThrowIf(_disposeTask != null, this);
            if (_cachedInfo != null) return ValueTaskExt.FromResult(_cachedInfo.Value);

            var host = KubeEnvironmentVars.KubernetesServiceHost;
            var port = KubeEnvironmentVars.KubernetesServicePort;
            var podIP = KubeEnvironmentVars.PodIP;
            var isKubeAvailable = !podIP.IsNullOrEmpty() && !host.IsNullOrEmpty() && port != 0;

            if (isKubeAvailable)
                _token = new KubeToken(Services, TokenPath);
            else if (Constants.DebugMode.KubeEmulation) {
                host = "this.host.does.not.exist";
                port = 53; // DNS port, definitely can't run Kubernetes service
                var urlMapper = Services.UrlMapper();
                podIP = urlMapper.BaseUri.Host;
                _token = new KubeToken(Services, "");
            }

            var info = _token != null
                ? new Kube(host, port, podIP, _token)
                : null;
            _cachedInfo = new CachedKube(info);
            return ValueTaskExt.FromResult(info);
        }
    }

    public async ValueTask<Kube> RequireKube(CancellationToken cancellationToken = default)
    {
        var info = await GetKube(cancellationToken).ConfigureAwait(false);
        return info ?? throw StandardError.NotSupported("This method should be executed within Kubernetes cluster.");
    }

    // Private members
    private static string ExecuteKubectlCommand(string arguments)
    {
        var startInfo = new ProcessStartInfo {
            FileName = "kubectl",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(startInfo);
        var output = process?.StandardOutput.ReadToEnd() ?? "";
        process?.WaitForExit();
        return output;
    }

    private static void SetupKubePermissions()
    {
        // Create a Role for managing leases in the default namespace
        const string roleYaml = """
            apiVersion: rbac.authorization.k8s.io/v1
            kind: Role
            metadata:
              namespace: default
              name: lease-manager
            rules:
            - apiGroups: ["coordination.k8s.io"]
              resources: ["leases"]
              verbs: ["get", "list", "watch", "create", "update", "patch", "delete"]
            - apiGroups: ["discovery.k8s.io"]
              resources: ["endpointslices"]
              verbs: ["get", "list", "watch"]
            """;
        var roleFile = Path.Combine(Path.GetTempPath(), "lease-manager-role.yaml");
        File.WriteAllText(roleFile, roleYaml);
        ExecuteKubectlCommand($"apply -f {roleFile}");

        // Bind the Role to the default ServiceAccount in the default namespace
        const string bindingYaml = """
            apiVersion: rbac.authorization.k8s.io/v1
            kind: RoleBinding
            metadata:
              namespace: default
              name: lease-manager-binding
            subjects:
            - kind: ServiceAccount
              name: default
              namespace: default
            roleRef:
              kind: Role
              name: lease-manager
              apiGroup: rbac.authorization.k8s.io
            """;
        var bindingFile = Path.Combine(Path.GetTempPath(), "lease-manager-binding.yaml");
        File.WriteAllText(bindingFile, bindingYaml);
        ExecuteKubectlCommand($"apply -f {bindingFile}");
    }

    // Nested types

    private record CachedKube(Kube? Value);
}
