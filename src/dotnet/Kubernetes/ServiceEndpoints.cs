namespace ActualChat.Kubernetes;

public record ServiceEndpoints(ServiceInfo Info, ImmutableArray<EndpointInfo> Endpoints, ImmutableArray<PortInfo> Ports);
