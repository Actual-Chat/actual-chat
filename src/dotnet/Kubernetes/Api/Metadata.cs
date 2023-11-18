namespace ActualChat.Kubernetes.Api;

public record Metadata(
    string ResourceVersion,
    string Name,
    string GenerateName,
    string Namespace,
    string Uid,
    int Generation,
    DateTime CreationTimestamp,
    Labels Labels,
    IReadOnlyList<OwnerReference> OwnerReferences
);
