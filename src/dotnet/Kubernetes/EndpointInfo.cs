namespace ActualChat.Kubernetes;

public record EndpointInfo(ImmutableArray<string> Addresses, bool IsReady);

