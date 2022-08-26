using System.Net;
using System.Net.Http.Headers;
using ActualChat.Kubernetes.Contract;
using ActualChat.Pooling;

namespace ActualChat.Kubernetes;

public class ServiceRegistry
{
    // private const string EndpointSlicesAPI =
    // "https://kubernetes.default.svc:443/apis/discovery.k8s.io/v1/{namespace}/default/endpointslices";
    private readonly SharedResourcePool<ServiceInfo, EndpointDiscoveryWorker> _discoveryWorkerPool;

    private IStateFactory StateFactory { get; }
    private HttpClient HttpClient { get; }

    public ServiceRegistry(IStateFactory stateFactory, HttpClient httpClient)
    {
        StateFactory = stateFactory;
        HttpClient = httpClient;
        _discoveryWorkerPool = new SharedResourcePool<ServiceInfo, EndpointDiscoveryWorker>(
            CreateEndpointDiscoveryWorker);
    }

    public async ValueTask<IMutableStateLease<ServiceEndpoints>> GetServiceEndpoints(
        string @namespace,
        string serviceName,
        CancellationToken cancellationToken)
    {
        if (!await KubernetesConfig.IsInCluster(cancellationToken))
            throw StandardError.NotSupported<ServiceRegistry>("Should be executed withing Kubernetes cluster");

        var worker = await _discoveryWorkerPool.Rent(new ServiceInfo(@namespace, serviceName), cancellationToken);
        return new MutableStateLease<ServiceEndpoints, ServiceInfo, IMutableState<ServiceEndpoints>,
            EndpointDiscoveryWorker>(
            worker,
            w => w.State);
    }

    private Task<EndpointDiscoveryWorker> CreateEndpointDiscoveryWorker(
        ServiceInfo serviceInfo,
        CancellationToken cancellationToken)
    {
        var worker = new EndpointDiscoveryWorker(serviceInfo, StateFactory, HttpClient);
        worker.Start();

        return Task.FromResult(worker);
    }

    private class EndpointDiscoveryWorker : WorkerBase
    {
        public IMutableState<ServiceEndpoints> State { get; }

        private ServiceInfo ServiceInfo { get; }
        private HttpClient HttpClient { get; }
        private IStateFactory StateFactory { get; }

        public EndpointDiscoveryWorker(
            ServiceInfo serviceInfo,
            IStateFactory stateFactory,
            HttpClient httpClient)
        {
            ServiceInfo = serviceInfo;
            StateFactory = stateFactory;
            HttpClient = httpClient;
            State = stateFactory.NewMutable(
                new ServiceEndpoints(
                    serviceInfo,
                    ImmutableArray<EndpointInfo>.Empty,
                    ImmutableArray<PortInfo>.Empty));
        }

        protected override async Task RunInternal(CancellationToken cancellationToken)
        {
            var config = await KubernetesConfig.Get(StateFactory, cancellationToken).ConfigureAwait(false);
            HttpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", config.Token.State.Value);

            using var requestMessage = new HttpRequestMessage(
                HttpMethod.Get,
        $"https://{config.Host}:{config.Port}/apis/discovery.k8s.io/v1/namespaces/{ServiceInfo.Namespace}/endpointslices");
            using var streamResponseMessage = await HttpClient
                .SendAsync(
                    requestMessage,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                .ConfigureAwait(false);

            if (streamResponseMessage.StatusCode == HttpStatusCode.Forbidden)
                throw StandardError.Constraint(
                    "Kubernetes ClusterRole to read EndpointSlices is required for the service account");

            streamResponseMessage.EnsureSuccessStatusCode();


            // await using var stream = await responseMessage.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            /*using (HttpRequestMessage requestMessage = request.BuildRequestMessage(HttpMethod.Get, (HttpContent) null, httpClient.BaseAddress))
      {
        requestMessage.MarkAsStreamed();
        streamedAsync = await httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead);
      }
      */
            // var endpointSliceList = JsonSerializer.DeserializeAsync<EndpointSliceList>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            // if (endpointSliceList == null)
            //     throw StandardError.Constraint("Unable to deserialize EndpointSliceList");

            // var content = await responseMessage.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            // var endpointSliceList = JsonSerializer.Deserialize<EndpointSliceList>(content);
            // if (endpointSliceList == null)
            //     throw StandardError.Constraint("Unable to deserialize EndpointSliceList");
            //
            // var serviceEndpointSlices = endpointSliceList.Items
            //     .Where(s => string.Equals(
            //         s.Metadata.Labels.ServiceName,
            //         ServiceInfo.ServiceName,
            //         StringComparison.Ordinal))
            //     .ToList();
            //
            // // TODO(AK): wait for at least one EndpointSlice for requested service
            // var endpointsMap = new Dictionary<string, (string Version, ImmutableArray<EndpointInfo> Endpoints)>();
            // var ports = serviceEndpointSlices
            //     .First()
            //     .Ports
            //     .Select(p => new PortInfo(p.Name, p.Protocol, p.Port))
            //     .ToImmutableArray();
            // foreach (var endpointSlice in serviceEndpointSlices) {
            //     endpointsMap[endpointSlice.Metadata.Name] =
            //         (
            //         endpointSlice.Metadata.ResourceVersion,
            //         endpointSlice
            //             .Endpoints
            //             .Select(e => new EndpointInfo(e.Addresses.ToImmutableArray(), e.Conditions.Ready))
            //             .ToImmutableArray()
            //         );
            //
            //     // JsonSerializer.DeserializeAsyncEnumerable<>()
            // }
            // foreach (var endpointSlice in serviceEndpointSlices) {
            //
            // }
        }
    }
}
