namespace ActualChat.Users.AppStores;

/// <summary>
/// iTunes Lookup API probe. It exposes the App Store version string, which is the build
/// version since releases are published under it; a two-part value from before ("2.17")
/// can't be compared with a build version and takes the train-only path.
/// </summary>
public sealed class AppleStoreProbe(IServiceProvider services) : StoreProbe(services)
{
    public override async Task<StoreProbeResult?> Probe(string storeId, CancellationToken cancellationToken)
    {
        var body = await Fetch(GetUri(storeId), cancellationToken).ConfigureAwait(false);
        return body is null ? null : Parse(body);
    }

    // Protected/internal methods

    // It's internal to be accessible from tests
    internal static Uri GetUri(string storeId)
        // Both stores publish to every storefront at once, so one storefront is the answer for all
        => new($"https://itunes.apple.com/lookup?bundleId={storeId.UrlEncode()}&country=us");

    // It's internal to be accessible from tests
    internal static StoreProbeResult? Parse(string body)
    {
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            throw StandardError.Format("The App Store lookup response has no 'results' array.");
        if (results.GetArrayLength() == 0)
            return null; // The app isn't sold in this storefront

        var result = results[0];
        if (!result.TryGetProperty("version", out var versionElement)
            || versionElement.GetString() is not { } storeVersion
            || storeVersion.IsNullOrEmpty())
            throw StandardError.Format("The App Store lookup result has no 'version'.");

        // Three parts is a build version (the release policy since v2.19); two is a marketing train
        var hasBuildVersion = storeVersion.Count(c => c == '.') >= 2;
        var buildVersion = hasBuildVersion && VersionExt.TryParseBuildVersion(storeVersion, out var v) ? v : null;
        var releasedAt = result.TryGetProperty("currentVersionReleaseDate", out var releasedAtElement)
            && releasedAtElement.TryGetDateTimeOffset(out var releasedAtValue)
            ? new Moment(releasedAtValue)
            : (Moment?)null;
        return new StoreProbeResult(storeVersion, buildVersion, releasedAt);
    }
}
