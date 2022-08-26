using System.Net;
using System.Net.Http.Headers;
using ActualChat.Kubernetes.Contract;
using ActualChat.Pooling;

namespace ActualChat.Kubernetes;

public class ServiceRegistry
{
    private readonly SharedResourcePool<ServiceInfo, EndpointDiscoveryWorker> _discoveryWorkerPool;

    private IStateFactory StateFactory { get; }
    private HttpClient HttpClient { get; }
    private ILogger<ServiceRegistry> Log { get; }

    public ServiceRegistry(IStateFactory stateFactory, HttpClient httpClient, ILogger<ServiceRegistry> log)
    {
        StateFactory = stateFactory;
        HttpClient = httpClient;
        Log = log;
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
        var worker = new EndpointDiscoveryWorker(serviceInfo, StateFactory, HttpClient, Log);
        worker.Start();

        return Task.FromResult(worker);
    }

    private class EndpointDiscoveryWorker : WorkerBase
    {
        public IMutableState<ServiceEndpoints> State { get; }

        private ServiceInfo ServiceInfo { get; }
        private HttpClient HttpClient { get; }
        private IStateFactory StateFactory { get; }
        private ILogger Log { get; }

        public EndpointDiscoveryWorker(
            ServiceInfo serviceInfo,
            IStateFactory stateFactory,
            HttpClient httpClient,
            ILogger log)
        {
            ServiceInfo = serviceInfo;
            StateFactory = stateFactory;
            HttpClient = httpClient;
            Log = log;
            State = stateFactory.NewMutable(
                new ServiceEndpoints(
                    serviceInfo,
                    ImmutableArray<EndpointInfo>.Empty,
                    ImmutableArray<PortInfo>.Empty));
        }

        protected override async Task RunInternal(CancellationToken cancellationToken)
        {
            var endpointsMap = new Dictionary<string, (EndpointSlice Slice, ImmutableArray<EndpointInfo> Endpoints)>();

            var config = await KubernetesConfig.Get(StateFactory, cancellationToken).ConfigureAwait(false);
            HttpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", config.Token.State.Value);

            using var requestMessage = new HttpRequestMessage(
                HttpMethod.Get,
        $"https://{config.Host}:{config.Port}/apis/discovery.k8s.io/v1/namespaces/{ServiceInfo.Namespace}/endpointslices"+
                    $"?watch=true&labelSelector=kubernetes.io/service-name%3D{ServiceInfo.ServiceName}");
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
            await using var stream = await streamResponseMessage.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var streamReader = new StreamReader(stream);

            // TODO(AK): add resilience and retries on failures plus error handling
            while (!streamReader.EndOfStream) {
                cancellationToken.ThrowIfCancellationRequested();

                var changeString = await streamReader.ReadLineAsync();
                if (changeString == null) {
                    Log.LogWarning("Got null during reading watch results");
                    continue;
                }
                var change = JsonSerializer.Deserialize<Contract.Change<EndpointSlice>>(changeString, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if (change == null) {
                    Log.LogWarning("Unable to deserialize watch result; {Change}", changeString);
                    continue;
                }
                switch (change.Type)
                {
                case ChangeType.Deleted:
                    endpointsMap.Remove(change.Object.Metadata.Name);
                    break;
                case ChangeType.Added:
                    endpointsMap[change.Object.Metadata.Name] = (
                        change.Object,
                        change.Object
                            .Endpoints
                            .Select(e => new EndpointInfo(e.Addresses.ToImmutableArray(), e.Conditions.Ready))
                            .ToImmutableArray()
                        );
                    break;
                case ChangeType.Modified:
                    endpointsMap[change.Object.Metadata.Name] = (
                        change.Object,
                        change.Object
                            .Endpoints
                            .Select(e => new EndpointInfo(e.Addresses.ToImmutableArray(), e.Conditions.Ready))
                            .ToImmutableArray()
                        );
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
                }

                var currentValue = State.Value;
                var newValue = new ServiceEndpoints(
                    ServiceInfo,
                    endpointsMap.Values
                        .SelectMany(p => p.Endpoints)
                        .ToImmutableArray(),
                    endpointsMap.Values
                        .SelectMany(p => p.Slice.Ports)
                        .Select(p => new PortInfo(p.Name, p.Protocol, p.Port))
                        .Distinct()
                        .ToImmutableArray());

                if (!currentValue.Equals(newValue))
                    State.Value = newValue;
            }
        }
    }
}
