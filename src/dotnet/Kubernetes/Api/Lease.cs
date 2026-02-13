using ActualChat.Kubernetes.Api.Internal;

namespace ActualChat.Kubernetes.Api;

#pragma warning disable CA1822

public record Lease(
    Metadata Metadata,
    LeaseSpec Spec
) {
    public string ApiVersion => "coordination.k8s.io/v1";
    public string Kind => "Lease";
}

public record LeaseSpec(
    string? HolderIdentity = null,
    int? LeaseDurationSeconds = null,
    [property: JsonConverter(typeof(NullableMicroTimeJsonConverter))]
    DateTime? AcquireTime = null,
    [property: JsonConverter(typeof(NullableMicroTimeJsonConverter))]
    DateTime? RenewTime = null,
    int? LeaseTransitions = null
);

public record LeaseList(
    Metadata Metadata,
    IReadOnlyList<Lease> Items
) {
    public string ApiVersion => "coordination.k8s.io/v1";
    public string Kind => "LeaseList";
}
