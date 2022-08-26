namespace ActualChat.Kubernetes.Contract;

public record OwnerReference(
    string ApiVersion,
    string Kind,
    string Name,
    string Uid,
    bool Controller,
    bool BlockOwnerDeletion
);
