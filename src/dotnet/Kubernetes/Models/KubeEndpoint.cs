namespace ActualChat.Kubernetes;

public record KubeEndpoint(ImmutableArray<string> Addresses, bool IsReady);
