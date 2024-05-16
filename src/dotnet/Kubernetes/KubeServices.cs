using System.Net;
using ActualChat.Kubernetes.Api;
using ActualChat.Pooling;

namespace ActualChat.Kubernetes;

public class KubeServices : IKubeInfo, IAsyncDisposable
{
    private static readonly JsonSerializerOptions WebJsonSerializeOptions = new(JsonSerializerDefaults.Web);
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
            CreateEndpointDiscoveryWorker) {
            ResourceDisposeDelay = TimeSpan.FromDays(3),
        };
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
        try {
            // We want to wait for the first update, otherwise we'll get an empty set of endpoints
            while (true) {
                var snapshot = state.Snapshot;
                if (!snapshot.IsInitial)
                    break;

                await snapshot.WhenUpdated().WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            return new MutableStateLease<
                KubeServiceEndpoints,
                KubeService,
                MutableState<KubeServiceEndpoints>,
                EndpointDiscoveryWorker>(lease, lease.Resource._state);
        }
        catch {
            lease.Dispose();
            throw;
        }
    }

    public ValueTask DisposeAsync()
        => _discoveryWorkerPool.DisposeAsync();

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
        // ReSharper disable once InconsistentNaming
        internal readonly MutableState<KubeServiceEndpoints> _state;

        public IState<KubeServiceEndpoints> State => _state;

        private IServiceProvider Services { get; }
        private Kube Kube { get; }
        private KubeService KubeService { get; }
        private ILogger Log { get; }

        public EndpointDiscoveryWorker(IServiceProvider services, Kube kube, KubeService kubeService)
        {
            Services = services;
            Log = services.LogFor(GetType());
            Kube = kube;
            KubeService = kubeService;

            var initialValue = new KubeServiceEndpoints(kubeService);
            _state = services.StateFactory().NewMutable(initialValue);
            this.Start();
        }

        protected override async Task OnRun(CancellationToken cancellationToken)
        {
            var retries = RetryDelaySeq.Exp(1, 10);
            var failureCount = 0;
            while (true) {
                try {
                    await UpdateState(cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false);
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
            Log.LogInformation("UpdateState: started for {Service}", KubeService);
            if (Kube.IsEmulated) {
                await UpdateEmulatedState(cancellationToken).ConfigureAwait(false);
                return;
            }

            var endpointsMap = new Dictionary<string, (EndpointSlice Slice, ApiArray<KubeEndpoint> Endpoints)>(StringComparer.Ordinal);

            using var httpClient = Kube.CreateHttpClient(Services.HttpClientFactory());
            var httpClientDisposable = new SafeDisposable(httpClient, 10, Log) { MustWait = false };
            await using var _ = httpClientDisposable.ConfigureAwait(false);

            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"apis/discovery.k8s.io/v1/namespaces/{KubeService.Namespace}/endpointslices" +
                    $"?watch=true&labelSelector=kubernetes.io/service-name%3D{KubeService.Name}");
            var response = await httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Forbidden)
                throw StandardError.Constraint(
                    "Kubernetes ClusterRole to read EndpointSlices is required for the service account.");
            response.EnsureSuccessStatusCode();

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false);
            using var streamReader = new StreamReader(stream);
            while (!streamReader.EndOfStream) {
#pragma warning disable CA2016
                // ReSharper disable once MethodSupportsCancellation
                var changeString = await streamReader.ReadLineAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
#pragma warning restore CA2016
                if (changeString == null) {
                    Log.LogWarning("UpdateState: got null while querying Kubernetes API result (endpoint slice changes)");
                    continue;
                }
                var change = JsonSerializer.Deserialize<Api.Change<EndpointSlice>>(changeString, WebJsonSerializeOptions);
                if (change == null) {
                    Log.LogWarning("UpdateState: unable to deserialize Kubernetes API result: {Change}", changeString);
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
                            .Select(e => new KubeEndpoint(e.Addresses.ToApiArray(), e.Conditions.Ready))
                            .ToApiArray()
                        );
                    break;
                case ChangeType.Modified:
                    endpointsMap[change.Object.Metadata.Name] = (
                        change.Object,
                        change.Object
                            .Endpoints
                            .Select(e => new KubeEndpoint(e.Addresses.ToApiArray(), e.Conditions.Ready))
                            .ToApiArray()
                        );
                    break;
                default:
                    throw StandardError.Constraint<Api.Change<EndpointSlice>>($"Type {change.Type} is invalid.");
                }

                var endpoints = endpointsMap.Values.SelectMany(p => p.Endpoints).ToApiArray();
                var readyEndpoints = endpoints.Where(e => e.IsReady).ToApiArray();
                var ports = endpointsMap.Values
                    .SelectMany(p => p.Slice.Ports)
                    .Select(p => new KubePort(p.Name, (KubeServiceProtocol)(int)p.Protocol, p.Port))
                    .Distinct()
                    .ToApiArray();
                var serviceEndpoints = new KubeServiceEndpoints(KubeService, endpoints, readyEndpoints, ports);

                cancellationToken.ThrowIfCancellationRequested();
                // delay update until we get some endpoints in ready state
                if (_state.Snapshot.IsInitial && serviceEndpoints.ReadyEndpoints.IsEmpty)
                    continue;

                _state.Value = serviceEndpoints;
                Log.LogInformation("UpdateState: service endpoints updated: {Endpoints}", serviceEndpoints);
            }
        }

        private async Task UpdateEmulatedState(CancellationToken cancellationToken)
        {
            var urlMapper = Services.UrlMapper();
            var port = urlMapper.BaseUri.Port;
            if (port == 0)
                port = 80;
            var ports = ApiArray.New(new KubePort("http", KubeServiceProtocol.Tcp, port));
            var addresses = ApiArray.New(Kube.PodIP);
            var endpoints = ApiArray.New(new KubeEndpoint(addresses, true));
            var serviceEndpoints = new KubeServiceEndpoints(KubeService, endpoints, endpoints, ports);

            _state.Value = serviceEndpoints;
            Log.LogInformation("UpdateState: service endpoints updated: {Endpoints}", serviceEndpoints);

            using var dTask = cancellationToken.ToTask();
            await dTask.Resource.ConfigureAwait(false);
        }
    }
}
