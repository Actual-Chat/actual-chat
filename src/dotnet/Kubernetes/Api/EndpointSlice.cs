namespace ActualChat.Kubernetes.Api;

public record EndpointSlice(
    Metadata Metadata,
    string AddressType,
    IReadOnlyList<Endpoint> Endpoints,
    IReadOnlyList<ServicePort> Ports
)
{
    public string ApiVersion => "discovery.k8s.io/v1";
    public string Kind => "EndpointSlice";
}
