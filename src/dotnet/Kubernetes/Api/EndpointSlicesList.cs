namespace ActualChat.Kubernetes.Api;

public record EndpointSliceList(
    string Kind,
    string ApiVersion,
    IReadOnlyList<EndpointSlice> Items
);
