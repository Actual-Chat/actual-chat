namespace ActualChat.Kubernetes.Api;

#pragma warning disable CA1724 // The type name Metadata conflicts in whole or in part with the namespace name ...

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
