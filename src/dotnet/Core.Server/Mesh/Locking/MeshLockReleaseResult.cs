namespace ActualChat.Mesh;

public enum MeshLockReleaseResult
{
    Released = 0,
    ExpiredOnRenewal,
    ExpiredOnRelease,
    AcquiredBySomeoneElse,
    UnknownError,
}
