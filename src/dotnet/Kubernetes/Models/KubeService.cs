namespace ActualChat.Kubernetes;

public sealed record KubeService(
    string Namespace,
    string Name);
