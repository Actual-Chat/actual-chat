namespace ActualChat.Kubernetes;

public record KubeService(
    string Namespace,
    string Name);
