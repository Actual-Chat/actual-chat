using System.Text.RegularExpressions;

namespace ActualChat.Users.AppStores;

/// <summary>
/// DisplayCatalog probe. The MSIX package name carries the nbgv version with a
/// trailing ".0", so the highest package version is directly comparable.
/// </summary>
public sealed partial class MicrosoftStoreProbe(IServiceProvider services) : StoreProbe(services)
{
    public override async Task<StoreProbeResult?> Probe(string storeId, CancellationToken cancellationToken)
    {
        var body = await Fetch(GetUri(storeId), cancellationToken).ConfigureAwait(false);
        return body is null ? null : Parse(body);
    }

    // Protected/internal methods

    // It's internal to be accessible from tests
    internal static Uri GetUri(string storeId)
        => new("https://displaycatalog.mp.microsoft.com/v7.0/products"
            + $"?bigIds={storeId.UrlEncode()}&market=US&languages=en-US");

    // It's internal to be accessible from tests
    internal static StoreProbeResult? Parse(string body)
    {
        using var json = JsonDocument.Parse(body);
        if (!json.RootElement.TryGetProperty("Products", out var products)
            || products.ValueKind != JsonValueKind.Array)
            throw StandardError.Format("The DisplayCatalog response has no 'Products' array.");
        if (products.GetArrayLength() == 0)
            return null; // The app isn't listed in this market

        var storeVersion = "";
        var buildVersion = VersionExt.Zero;
        var releasedAt = (Moment?)null;
        foreach (var product in products.EnumerateArray()) {
            if (!product.TryGetProperty("DisplaySkuAvailabilities", out var skuAvailabilities)
                || skuAvailabilities.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var skuAvailability in skuAvailabilities.EnumerateArray()) {
                if (!skuAvailability.TryGetProperty("Sku", out var sku))
                    continue;
                if (!sku.TryGetProperty("Properties", out var properties)
                    || !properties.TryGetProperty("Packages", out var packages)
                    || packages.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var package in packages.EnumerateArray()) {
                    if (!package.TryGetProperty("PackageFullName", out var packageFullName)
                        || packageFullName.GetString() is not { } fullName)
                        continue;

                    var match = PackageVersionRegex().Match(fullName);
                    if (!match.Success
                        || !VersionExt.TryParseBuildVersion(match.Groups[1].Value, out var packageVersion)
                        || packageVersion <= buildVersion)
                        continue;

                    storeVersion = match.Groups[1].Value;
                    buildVersion = packageVersion;
                    releasedAt = sku.TryGetProperty("LastModifiedDate", out var lastModifiedAt)
                        && lastModifiedAt.TryGetDateTimeOffset(out var lastModifiedAtValue)
                        ? new Moment(lastModifiedAtValue)
                        : null;
                }
            }
        }

        if (storeVersion.IsNullOrEmpty())
            throw StandardError.Format("The DisplayCatalog response lists no versioned package.");

        return new StoreProbeResult(storeVersion, buildVersion, releasedAt);
    }

    // Private methods

    [GeneratedRegex(@"_(\d+\.\d+\.\d+(?:\.\d+)?)_")]
    private static partial Regex PackageVersionRegex();
}
