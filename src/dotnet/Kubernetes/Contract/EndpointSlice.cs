namespace ActualChat.Kubernetes.Contract;

public record EndpointSlice(
    Metadata Metadata,
    string AddressType,
    IReadOnlyList<Endpoint> Endpoints,
    IReadOnlyList<ServicePort> Ports
);
