namespace ActualChat.Kubernetes.Contract;

public record ServicePort(
    string Name,
    ServiceProtocol Protocol,
    int Port
)
{
    public string? AppProtocol { get; init; }
}
