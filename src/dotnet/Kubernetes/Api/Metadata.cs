namespace ActualChat.Kubernetes.Api;

public sealed record Metadata(
    string Name,
    string? Namespace = null,
    string? ResourceVersion = null,
    string? GenerateName = null,
    string? Uid = null,
    long? Generation = null,
    DateTime? CreationTimestamp = null,
    Labels? Labels = null,
    Annotations? Annotations = null,
    IReadOnlyList<OwnerReference>? OwnerReferences = null
);
