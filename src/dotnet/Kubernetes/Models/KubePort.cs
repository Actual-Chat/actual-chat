namespace ActualChat.Kubernetes;

public record KubePort(
    string Name,
    KubeServiceProtocol Protocol,
    int Port);
