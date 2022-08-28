namespace ActualChat.Kubernetes.Api;

public record OwnerReference(
    string ApiVersion,
    string Kind,
    string Name,
    string Uid,
    bool Controller,
    bool BlockOwnerDeletion
);
