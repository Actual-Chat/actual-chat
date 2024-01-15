namespace ActualChat.ServiceMesh;

public record ClusterLockInfo(
    Symbol Key,
    string Value,
    string HolderId);
