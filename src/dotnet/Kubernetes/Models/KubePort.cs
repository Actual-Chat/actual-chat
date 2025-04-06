namespace ActualChat.Kubernetes;

public sealed record KubePort(
    string Name,
    KubeServiceProtocol Protocol,
    int Port);
