using System.Net;
using ActualChat.Kubernetes.Api;
using ActualChat.Pooling;

namespace ActualChat.Kubernetes;

public class KubeServices : IKubeInfo
{
    private readonly SharedResourcePool<KubeService, EndpointDiscoveryWorker> _discoveryWorkerPool;

    private IServiceProvider Services { get; }
    private IKubeInfo KubeInfo { get; }
    private ILogger Log { get; }

    public KubeServices(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());

        KubeInfo = services.KubeInfo();
        _discoveryWorkerPool = new SharedResourcePool<KubeService, EndpointDiscoveryWorker>(
            CreateEndpointDiscoveryWorker);
    }

    public ValueTask<Kube?> GetKube(CancellationToken cancellationToken = default)
        => KubeInfo.GetKube(cancellationToken);

    public ValueTask<bool> HasKube(CancellationToken cancellationToken = default)
        => KubeInfo.HasKube(cancellationToken);

    public ValueTask<Kube> RequireKube(CancellationToken cancellationToken = default)
        => KubeInfo.RequireKube(cancellationToken);

    public async ValueTask<IMutableStateLease<KubeServiceEndpoints>> GetServiceEndpoints(
        KubeService kubeService,
        CancellationToken cancellationToken)
    {
        await KubeInfo.RequireKube(cancellationToken).ConfigureAwait(false);
        var lease = await _discoveryWorkerPool.Rent(kubeService, cancellationToken).ConfigureAwait(false);
        var state = lease.Resource.State;
        // We want to wait for the first update, otherwise we'll get an empty set of endpoints
        while (state.Snapshot.UpdateCount == 0)
            await state.Computed.WhenInvalidated(cancellationToken).ConfigureAwait(false);
        return new MutableStateLease<
            KubeServiceEndpoints,
            KubeService,
            IMutableState<KubeServiceEndpoints>,
            EndpointDiscoveryWorker>(lease, w => w.State);
    }

    // Private methods

    private async Task<EndpointDiscoveryWorker> CreateEndpointDiscoveryWorker(
        KubeService kubeService,
        CancellationToken cancellationToken)
    {
        var kube = await KubeInfo.RequireKube(cancellationToken).ConfigureAwait(false);
        var worker = new EndpointDiscoveryWorker(Services, kube, kubeService);
        return worker;
    }

    // Nested types

    private sealed class EndpointDiscoveryWorker : WorkerBase
    {
        public IMutableState<KubeServiceEndpoints> State { get; }

        private IServiceProvider Services { get; }
        private Kube Kube { get; }
        private KubeService KubeService { get; }
        private ILogger Log { get; }

        public EndpointDiscoveryWorker(IServiceProvider services, Kube kube, KubeService kubeService)
        {
            Services = services;
            Kube = kube;
            KubeService = kubeService;
            Log = services.LogFor(GetType());

            var initialValue = new KubeServiceEndpoints(
                kubeService,
                ImmutableArray<KubeEndpoint>.Empty,
                ImmutableArray<KubeEndpoint>.Empty,
                ImmutableArray<KubePort>.Empty);
            State = services.StateFactory().NewMutable(initialValue);
            Start();
        }

        protected override async Task RunInternal(CancellationToken cancellationToken)
        {
            var retries = new RetryDelaySeq(1, 10);
            var failureCount = 0;
            while (true) {
                try {
                    Log.LogInformation("UpdateState started for {Service}", KubeService);
                    await UpdateState(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e) when (e is not OperationCanceledException) {
                    Log.LogError(e, "UpdateState failed for service {Service}", KubeService);
                    failureCount++;
                    await Task.Delay(retries[failureCount], cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private async Task UpdateState(CancellationToken cancellationToken)
        {
            var endpointsMap = new Dictionary<string, (EndpointSlice Slice, ImmutableArray<KubeEndpoint> Endpoints)>(StringComparer.Ordinal);

            // TODO(AY,AK): It makes sense to add watch loop here - with resilience and retries on failures

            using var httpClient = Kube.CreateHttpClient(Services.HttpClientFactory());
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"apis/discovery.k8s.io/v1/namespaces/{KubeService.Namespace}/endpointslices" +
                    $"?watch=true&labelSelector=kubernetes.io/service-name%3D{KubeService.Name}");
            using var response = await httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.Forbidden)
                throw StandardError.Constraint(
                    "Kubernetes ClusterRole to read EndpointSlices is required for the service account");

            response.EnsureSuccessStatusCode();

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var _ = stream.ConfigureAwait(false);
            using var streamReader = new StreamReader(stream);

            while (!streamReader.EndOfStream) {
                var changeString = await streamReader.ReadLineAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
                if (changeString == null) {
                    Log.LogWarning("Got null while querying Kubernetes API result (endpoint slice changes)");
                    continue;
                }
                #pragma warning disable IL2026
                var change = JsonSerializer.Deserialize<Api.Change<EndpointSlice>>(
                    changeString,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if (change == null) {
                    Log.LogWarning("Unable to deserialize Kubernetes API result: {Change}", changeString);
                    continue;
                }
                switch (change.Type) {
                case ChangeType.Deleted:
                    endpointsMap.Remove(change.Object.Metadata.Name);
                    break;
                case ChangeType.Added:
                    endpointsMap[change.Object.Metadata.Name] = (
                        change.Object,
                        change.Object
                            .Endpoints
                            .Select(e => new KubeEndpoint(e.Addresses.ToImmutableArray(), e.Conditions.Ready))
                            .ToImmutableArray()
                        );
                    break;
                case ChangeType.Modified:
                    endpointsMap[change.Object.Metadata.Name] = (
                        change.Object,
                        change.Object
                            .Endpoints
                            .Select(e => new KubeEndpoint(e.Addresses.ToImmutableArray(), e.Conditions.Ready))
                            .ToImmutableArray()
                        );
                    break;
                default:
                    throw StandardError.Constraint<Api.Change<EndpointSlice>>($"Type {change.Type} is invalid.");
                }

                var endpoints = endpointsMap.Values.SelectMany(p => p.Endpoints).ToImmutableArray();
                var readyEndpoints = endpoints.Where(e => e.IsReady).ToImmutableArray();
                var ports = endpointsMap.Values
                    .SelectMany(p => p.Slice.Ports)
                    .Select(p => new KubePort(p.Name, (KubeServiceProtocol)(int)p.Protocol, p.Port))
                    .Distinct()
                    .ToImmutableArray();
                var serviceEndpoints = new KubeServiceEndpoints(KubeService, endpoints, readyEndpoints, ports);

                State.Value = serviceEndpoints;
                Log.LogInformation("Kubernetes service endpoints updated: {Endpoints}", serviceEndpoints);
            }
        }
    }
}
