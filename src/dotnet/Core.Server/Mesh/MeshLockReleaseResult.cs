namespace ActualChat.Mesh;

public enum MeshLockReleaseResult
{
    Released = 0,
    NotAcquired,
    AcquiredBySomeoneElse,
    Unknown,
}
