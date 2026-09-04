namespace ActualChat.Users;

/// <summary>
/// Reports the newest build known to be published in the store for a given app kind,
/// so a client running an older build can offer an update.
/// </summary>
public interface IAppUpdates : IComputeService
{
    // null means "unknown" - the answer on non-production instances and until the first probe lands
    [ComputeMethod]
    Task<AppUpdateInfo?> GetLatestUpdateInfo(AppKind appKind, CancellationToken cancellationToken);
}

// Version is the build version clients compare themselves against;
// StoreVersion is whatever the store displays, and is never comparable.

[DataContract, MessagePackObject]
public sealed partial record AppUpdateInfo(
    [property: DataMember, Key(0)] AppKind AppKind,
    [property: DataMember, Key(1)] string Version,
    [property: DataMember, Key(2)] string StoreVersion,
    [property: DataMember, Key(3)] Moment ReleasedAt,
    [property: DataMember, Key(4)] Moment DetectedAt);
