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
        _discoveryWorkerPool = new SharedResourcePool<KubeService, EndpointDiscoveryWorker>(CreateEndpointDiscoveryWorker) {
            ResourceDisposeDelay = TimeSpan.FromDays(3),
        };
    }

    public ValueTask<Kube?> GetKube(CancellationToken cancellationToken = default)
        => KubeInfo.GetKube(cancellationToken);

    public ValueTask<Kube> RequireKube(CancellationToken cancellationToken = default)
        => KubeInfo.RequireKube(cancellationToken);

    public async ValueTask<SharedResourcePool<KubeService, EndpointDiscoveryWorker>.Lease> GetServiceEndpoints(
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
            return lease;
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

    public sealed class EndpointDiscoveryWorker : WorkerBase
    {
        // ReSharper disable once InconsistentNaming
        private readonly MutableState<KubeServiceEndpoints> _state;

        public IState<KubeServiceEndpoints> State => _state;

        private IServiceProvider Services { get; }
        private IHttpClientFactory HttpClientFactory { get; }
        private Kube Kube { get; }
        private KubeService KubeService { get; }
        private ILogger Log { get; }

        public EndpointDiscoveryWorker(IServiceProvider services, Kube kube, KubeService kubeService)
        {
            Services = services;
            HttpClientFactory = services.HttpClientFactory();
            Log = services.LogFor(GetType());
            Kube = kube;
            KubeService = kubeService;

            var initialValue = new KubeServiceEndpoints(kubeService);
            _state = services.StateFactory().NewMutable(initialValue);
            this.Start();
        }

        protected override async Task OnRun(CancellationToken cancellationToken)
        {
            var resourceVersion = "";
            var failureCount = 0;
            var retryDelays = RetryDelaySeq.Exp(1, 30);

            while (!cancellationToken.IsCancellationRequested) {
                try {
                    resourceVersion = await UpdateState(resourceVersion, cancellationToken).ConfigureAwait(false);
                    failureCount = 0;
                }
                catch (Exception e) when (e is not OperationCanceledException) {
                    if (e is HttpRequestException { StatusCode: HttpStatusCode.Gone }) {
                        resourceVersion = "";
                        continue;
                    }

                    Log.LogError(e, "UpdateState failed for service {Service}", KubeService);
                    await Task.Delay(retryDelays[++failureCount], cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private async Task<string> UpdateState(string resourceVersion, CancellationToken cancellationToken)
        {
            Log.LogInformation("UpdateState: started for {Service}, resourceVersion: {ResourceVersion}", KubeService, resourceVersion);
            if (Kube.IsEmulated) {
                UpdateEmulatedState();
                await TaskExt.NeverEnding(cancellationToken).ConfigureAwait(false);
                return resourceVersion;
            }

            var endpointsMap = new Dictionary<string, (EndpointSlice Slice, KubeEndpoint[] Endpoints)>();

            using var httpClient = Kube.CreateHttpClient(HttpClientFactory);
            if (resourceVersion.IsNullOrEmpty()) {
                var listUrl = $"apis/discovery.k8s.io/v1/namespaces/{KubeService.Namespace}/endpointslices" +
                              $"?labelSelector=kubernetes.io/service-name%3D{KubeService.Name}";
                var listResponse = await httpClient.GetAsync(listUrl, cancellationToken).ConfigureAwait(false);
                listResponse.EnsureSuccessStatusCode();
                var list = await listResponse.Content.ReadFromJsonAsync<EndpointSliceList>(WebJsonSerializeOptions, cancellationToken).ConfigureAwait(false);
                if (list?.Metadata != null) {
                    resourceVersion = list.Metadata.ResourceVersion ?? "";
                    foreach (var item in list.Items)
                        ApplyChange(ChangeType.Added, item, endpointsMap);
                }
            }

            var url = $"apis/discovery.k8s.io/v1/namespaces/{KubeService.Namespace}/endpointslices" +
                      $"?watch=true&labelSelector=kubernetes.io/service-name%3D{KubeService.Name}" +
                      $"&resourceVersion={resourceVersion}&allowWatchBookmarks=true&timeoutSeconds=300";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            var response = await httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Gone)
                return "";

            if (response.StatusCode == HttpStatusCode.Forbidden)
                throw StandardError.Constraint(
                    "Kubernetes ClusterRole to read EndpointSlices is required for the service account.");
            response.EnsureSuccessStatusCode();

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var streamReader = new StreamReader(stream);
            while (true) {
                var changeString = await streamReader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (changeString == null)
                    break; // End of stream
                if (changeString.Length == 0)
                    continue; // Empty line

                var watchEvent = JsonSerializer.Deserialize<Api.Change<EndpointSlice>>(changeString, WebJsonSerializeOptions);
                if (watchEvent == null)
                    continue;

                if (watchEvent.Object is {} endpointSlice) {
                    if (!endpointSlice.Metadata.ResourceVersion.IsNullOrEmpty())
                        resourceVersion = endpointSlice.Metadata.ResourceVersion;

                    if (watchEvent.Type is not (ChangeType.Bookmark or ChangeType.Error))
                        ApplyChange(watchEvent.Type, endpointSlice, endpointsMap);
                }
                else if (watchEvent.Type == ChangeType.Error)
                    return "";
            }
            return resourceVersion ?? "";
        }

        private void ApplyChange(ChangeType changeType, EndpointSlice endpointSlice, Dictionary<string, (EndpointSlice Slice, KubeEndpoint[] Endpoints)> endpointsMap)
        {
            switch (changeType) {
            case ChangeType.Deleted:
                endpointsMap.Remove(endpointSlice.Metadata.Name);
                break;
            case ChangeType.Added:
            case ChangeType.Modified:
                endpointsMap[endpointSlice.Metadata.Name] = (
                    endpointSlice,
                    endpointSlice
                        .Endpoints
                        .Select(e => new KubeEndpoint(e.Addresses.ToArray(), e.Conditions?.Ready ?? false))
                        .ToArray()
                );
                break;
            default:
                return;
            }

            var endpoints = endpointsMap.Values.SelectMany(p => p.Endpoints).ToArray();
            var readyEndpoints = endpoints.Where(e => e.IsReady).ToArray();
            var ports = endpointsMap.Values
                .SelectMany(p => p.Slice.Ports)
                .Select(p => new KubePort(p.Name ?? "", (KubeServiceProtocol)(int)(p.Protocol ?? ServiceProtocol.TCP), p.Port ?? 0))
                .Distinct()
                .ToArray();
            var serviceEndpoints = new KubeServiceEndpoints(KubeService, endpoints, readyEndpoints, ports);

            // delay update until we get some endpoints in ready state
            if (_state.Snapshot.IsInitial && serviceEndpoints.ReadyEndpoints.Length == 0)
                return;

            _state.Value = serviceEndpoints;
            Log.LogInformation("UpdateState: service endpoints updated: {Endpoints}", serviceEndpoints);
        }

        private void UpdateEmulatedState()
        {
            var urlMapper = Services.UrlMapper();
            var port = urlMapper.BaseUri.Port;
            if (port == 0)
                port = 80;

            var addresses = new[] { Kube.PodIP };
            var endpoints = new[] { new KubeEndpoint(addresses, true) };
            var ports = new[] { new KubePort("http", KubeServiceProtocol.Tcp, port) };
            var serviceEndpoints = new KubeServiceEndpoints(KubeService, endpoints, endpoints, ports);

            _state.Value = serviceEndpoints;
            Log.LogInformation("UpdateState: service endpoints updated: {Endpoints}", serviceEndpoints);
        }
    }
}
