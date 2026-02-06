namespace ActualChat.Mesh;

/// <summary>
/// Information about a distributed mesh lock.
/// </summary>
public record MeshLockInfo(
    string Key,
    string HolderId);
