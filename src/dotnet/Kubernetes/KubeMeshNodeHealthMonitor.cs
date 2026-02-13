using ActualChat.Mesh;

namespace ActualChat.Kubernetes;

public sealed class KubeMeshNodeHealthMonitor : WorkerBase
{
    private IServiceProvider Services { get; }
    private KubeServices KubeServices { get; }
    private MeshWatcher MeshWatcher { get; }
    private ILogger Log { get; }

    private string ServiceName { get; }
    private string Namespace { get; }
    private string OwnIP { get; }

    public KubeMeshNodeHealthMonitor(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());
        KubeServices = services.GetRequiredService<KubeServices>();
        MeshWatcher = services.MeshWatcher();

        ServiceName = KubeEnvironmentVars.KubeServiceName;
        Namespace = KubeEnvironmentVars.PodNamespace;
        OwnIP = KubeEnvironmentVars.PodIP;

        if (!ServiceName.IsNullOrEmpty())
            this.Start();
        else
            Log.LogInformation("Disabled: KUBE_SERVICE_NAME is not set");
    }

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var kube = await KubeServices.GetKube(cancellationToken).ConfigureAwait(false);
        if (kube == null || kube.IsEmulated) {
            Log.LogInformation("Disabled: Kubernetes is not available");
            return;
        }

        // Wait for MeshWatcher to announce before monitoring
        await MeshWatcher.WhenAnnounced.WaitAsync(cancellationToken).ConfigureAwait(false);
        Log.LogInformation("Started monitoring EndpointSlice for service {ServiceName}", ServiceName);

        var kubeService = new KubeService(Namespace, ServiceName);
        var lease = await KubeServices.GetServiceEndpoints(kubeService, cancellationToken).ConfigureAwait(false);
        try {
            var state = lease.Resource.State;
            var previousIPs = CollectAllIPs(state.Value);
            Log.LogInformation("Initial endpoint IPs: {IPCount}", previousIPs.Count);

            while (!cancellationToken.IsCancellationRequested) {
                var snapshot = state.Snapshot;
                await snapshot.WhenUpdated().WaitAsync(cancellationToken).ConfigureAwait(false);

                var currentIPs = CollectAllIPs(state.Value);
                DetectRemovedIPs(previousIPs, currentIPs);
                previousIPs = currentIPs;
            }
        }
        finally {
            lease.Dispose();
        }
    }

    private static HashSet<string> CollectAllIPs(KubeServiceEndpoints endpoints)
    {
        var ips = new HashSet<string>(StringComparer.Ordinal);
        foreach (var endpoint in endpoints.Endpoints)
            foreach (var address in endpoint.Addresses)
                ips.Add(address);
        return ips;
    }

    private void DetectRemovedIPs(HashSet<string> oldIPs, HashSet<string> newIPs)
    {
        foreach (var ip in oldIPs) {
            if (newIPs.Contains(ip))
                continue;

            // Skip own IP
            if (OrdinalEquals(ip, OwnIP))
                continue;

            Log.LogWarning("Pod IP removed from EndpointSlice: {IP}", ip);
            ConfirmDeadByIP(ip);
        }
    }

    private void ConfirmDeadByIP(string ip)
    {
        var meshState = MeshWatcher.State.LastNonErrorValue;
        foreach (var node in meshState.AllNodes.Values) {
            if (node.State is MeshNodeState.Dead)
                continue;

            // MeshNode.Endpoint is "{host}:{port}" - extract host
            var endpoint = node.Endpoint;
            var colonIndex = endpoint.LastIndexOf(':');
            var host = colonIndex >= 0 ? endpoint[..colonIndex] : endpoint;

            if (OrdinalEquals(host, ip)) {
                Log.LogWarning("Confirming node dead via EndpointSlice: {Node}", node);
                MeshWatcher.ConfirmNodeDead(endpoint);
            }
        }
    }
}
