namespace ActualChat.Mesh;

public enum MeshLockReleaseResult
{
    Released = 0,
    MarkedAsExpiredEarlier,
    Expired,
    NotAcquired,
    AcquiredBySomeoneElse,
    Unknown,
}
