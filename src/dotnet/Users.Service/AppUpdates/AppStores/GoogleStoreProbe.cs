using System.Text.RegularExpressions;

namespace ActualChat.Users.AppStores;

/// <summary>
/// Google Play has no public JSON API, so this reads the store page and pulls the
/// "About this app - Version" data block, which carries the full versionName.
/// </summary>
public sealed partial class GoogleStoreProbe(IServiceProvider services) : StoreProbe(services)
{
    public override async Task<StoreProbeResult?> Probe(string storeId, CancellationToken cancellationToken)
    {
        var body = await Fetch(GetUri(storeId), cancellationToken).ConfigureAwait(false);
        return body is null ? null : Parse(body);
    }

    // Protected/internal methods

    // It's internal to be accessible from tests
    internal static Uri GetUri(string storeId)
        => new($"https://play.google.com/store/apps/details?id={storeId.UrlEncode()}&hl=en&gl=US");

    // It's internal to be accessible from tests
    internal static StoreProbeResult Parse(string body)
    {
        // Every other X.Y.Z on the page is review metadata, so only the [[["..."]]] shape counts,
        // and anything but exactly one match is a page change we must not read as a version.
        var matches = VersionBlockRegex().Matches(body);
        if (matches.Count != 1)
            throw StandardError.Format($"The Play store page has {matches.Count} version blocks, expected 1.");

        var storeVersion = matches[0].Groups[1].Value;
        if (!VersionExt.TryParseBuildVersion(storeVersion, out var buildVersion))
            throw StandardError.Format($"The Play store page version '{storeVersion}' isn't parseable.");

        return new StoreProbeResult(storeVersion, buildVersion, null);
    }

    // Private methods

    [GeneratedRegex(@"\[\[\[""(\d+\.\d+\.\d+)""\]\]")]
    private static partial Regex VersionBlockRegex();
}
