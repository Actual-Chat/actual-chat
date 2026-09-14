namespace ActualChat.Users;

// Version is whatever the store displays ("2.17", "2.17.246", "2.17.246.0") normalized to the
// three components a client can compare itself against, and VersionString is that normal form.
public sealed record AppStoreProbeResult(Version Version, Moment? ReleasedAt)
{
    public string VersionString => field ??= Version.ToString();

    private static Version ParseVersionString(string versionString)
    {
        if (!VersionExt.TryParseBuildVersion(versionString, out var version))
            throw StandardError.Format($"The version '{versionString}' isn't parseable.");

        // "2.17" is what the App Store showed before releases moved to the build version
        return versionString.Count(c => c == '.') >= 2
            ? version
            : new Version(version.Major, version.Minor, 9999);
    }

    public AppStoreProbeResult(string version, Moment? releasedAt)
        : this(ParseVersionString(version), releasedAt)
    { }

    private AppStoreProbeResult(AppStoreProbeResult other)
    {
        Version = other.Version;
        ReleasedAt = other.ReleasedAt;
        VersionString = null!; // Recomputed on demand rather than copied, so `with` can't stale it
    }

    // This record relies on referential equality: VersionString's backing field is populated
    // on read, so the generated Equals would call two identical instances different
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
    public bool Equals(AppStoreProbeResult? other) => ReferenceEquals(this, other);
}
