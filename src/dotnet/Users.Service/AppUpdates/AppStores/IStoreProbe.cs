namespace ActualChat.Users.AppStores;

/// <summary>
/// Asks one store what it currently serves for an app id.
/// </summary>
public interface IStoreProbe
{
    // null means "the app isn't listed"; anything unexpected throws, because
    // "we couldn't read the page" must never be reported as "nothing published"
    Task<StoreProbeResult?> Probe(string storeId, CancellationToken cancellationToken);
}

// BuildVersion is null for a store that shows a marketing version only (the App Store's "2.17"),
// which is what forces the train-only detection path.

[StructLayout(LayoutKind.Auto)]
public readonly record struct StoreProbeResult(
    string StoreVersion,
    Version? BuildVersion,
    Moment? ReleasedAt);
