namespace ActualChat.Kubernetes.Api;

public record EndpointSliceList(
    Metadata Metadata,
    IReadOnlyList<EndpointSlice> Items
)
{
    public string ApiVersion => "discovery.k8s.io/v1";
    public string Kind => "EndpointSliceList";
}

