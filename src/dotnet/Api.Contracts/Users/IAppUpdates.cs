namespace ActualChat.Users;

/// <summary>
/// Reports the newest build known to be published in the store for a given app kind,
/// so a client running an older build can offer an update.
/// </summary>
public interface IAppUpdates : IComputeService
{
    // null means "unknown" - the answer on non-production instances and until the first check lands.
    // Consolidation keeps a re-check that found nothing from reaching clients as a fake update.
    [ComputeMethod(ConsolidationDelay = 0)]
    Task<AppUpdateInfo?> GetLatestUpdateInfo(AppKind appKind, CancellationToken cancellationToken);
}

// VersionString is the build version clients compare themselves against; StoreVersionString is
// whatever the store displays, and is never comparable - so it gets no parsed counterpart.

[DataContract, MessagePackObject]
public sealed partial record AppUpdateInfo(
    [property: DataMember, Key(0)] AppKind AppKind,
    [property: DataMember, Key(1)] string VersionString,
    [property: DataMember, Key(2)] string StoreVersionString,
    [property: DataMember, Key(3)] Moment ReleasedAt,
    [property: DataMember, Key(4)] Moment DetectedAt)
{
    // Deliberately not a `field ??=` cache: GetLatestUpdateInfo consolidates, and a record's
    // Equals compares every instance field - see AppUpdateInfoTest
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public Version Version => VersionExt.ParseBuildVersion(VersionString);

    public AppUpdateInfo(AppKind AppKind, string VersionString, Moment ReleasedAt, Moment DetectedAt)
        : this(AppKind, VersionString, VersionString, ReleasedAt, DetectedAt)
    { }
}
