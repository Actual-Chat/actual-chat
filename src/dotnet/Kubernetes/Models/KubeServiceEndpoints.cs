namespace ActualChat.Kubernetes;

public record KubeServiceEndpoints(
    KubeService Service,
    ImmutableArray<KubeEndpoint> Endpoints,
    ImmutableArray<KubePort> Ports);
