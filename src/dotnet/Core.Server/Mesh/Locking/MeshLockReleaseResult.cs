namespace ActualChat.Mesh;

public enum MeshLockReleaseResult
{
    Released = 0,
    Expired,
    NotAcquired,
    AcquiredBySomeoneElse,
    Unknown,
}
