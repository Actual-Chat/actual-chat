namespace ActualChat.Kubernetes.Api;

public sealed record ServicePort(
    string? Name,
    ServiceProtocol? Protocol,
    int? Port)
{
    public string? AppProtocol { get; init; }
}
