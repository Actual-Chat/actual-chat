using System.Text.RegularExpressions;

namespace ActualChat.Users;

// null means "the app isn't listed"; anything unexpected throws, because
// "we couldn't read the page" must never be reported as "nothing published"
public delegate Task<AppStoreProbeResult> StoreProbe(string storeId, CancellationToken cancellationToken);

/// <summary>
/// Asks each store what it currently serves for an app id: one probe per store, plus the
/// HTTP plumbing they share. <see cref="Get"/> maps an <see cref="AppKind"/> to its probe.
/// </summary>
public partial class AppStoreProbes(IServiceProvider services)
{
    public const string HttpClientName = nameof(StoreProbe);
    // Google Play answers anything else with a consent stub that carries no version
    private const string UserAgentValue =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) "
        + "Chrome/128.0.0.0 Safari/537.36";
    private const int MaxResponseSize = 8 * 1024 * 1024;

    // {0} is the store id. One storefront is asked for everyone: all three stores publish to
    // every storefront at once. MacOS shares the iOS entries - Mac Catalyst is a universal
    // purchase on the same App ID. They're internal to be accessible from tests.
    internal const string AppleUriFormat = "https://itunes.apple.com/lookup?bundleId={0}&country=us";
    internal const string GoogleUriFormat =
        "https://play.google.com/store/apps/details?id={0}&hl=en&gl=US";
    internal const string MicrosoftUriFormat = "https://displaycatalog.mp.microsoft.com/v7.0/products"
        + "?bigIds={0}&market=US&languages=en-US";
    private static readonly IReadOnlyDictionary<AppKind, string> UriFormats =
        new Dictionary<AppKind, string> {
            { AppKind.Ios, AppleUriFormat },
            { AppKind.MacOS, AppleUriFormat },
            { AppKind.Android, GoogleUriFormat },
            { AppKind.Windows, MicrosoftUriFormat },
        };
    private static readonly IReadOnlyDictionary<AppKind, Func<string, AppStoreProbeResult>> Parsers =
        new Dictionary<AppKind, Func<string, AppStoreProbeResult>> {
            { AppKind.Ios, ParseApple },
            { AppKind.MacOS, ParseApple },
            { AppKind.Android, ParseGoogle },
            { AppKind.Windows, ParseMicrosoft },
        };

    private IServiceProvider Services { get; } = services;
    private IHttpClientFactory HttpClientFactory => field ??= Services.HttpClientFactory();

    // It's virtual so tests can script the probes
    public virtual StoreProbe? Get(AppKind appKind)
    {
        if (!(UriFormats.TryGetValue(appKind, out var uriFormat) && Parsers.TryGetValue(appKind, out var parse)))
            return null;

        return async (storeId, cancellationToken) => {
            var storeUri = new Uri(string.Format(uriFormat, storeId.UrlEncode()));
            var body = await Fetch(storeUri, cancellationToken).ConfigureAwait(false);
            return parse.Invoke(body);
        };
    }

    // Protected/internal methods

    // It's internal to be accessible from tests. The iTunes Lookup API's version string is the
    // build version: releases are published under it (ensure_app_store_version in the Fastfile).
    internal static AppStoreProbeResult ParseApple(string body)
    {
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            throw StandardError.Format("The App Store lookup response has no 'results' array.");
        if (results.GetArrayLength() == 0)
            throw StandardError.Format("The app isn't sold in this storefront.");

        var result = results[0];
        if (!result.TryGetProperty("version", out var versionElement)
            || versionElement.GetString() is not { } versionString
            || versionString.IsNullOrEmpty())
            throw StandardError.Format("The App Store lookup result has no 'version'.");

        var releasedAt = result.TryGetProperty("currentVersionReleaseDate", out var releasedAtElement)
            && releasedAtElement.TryGetDateTimeOffset(out var releasedAtValue)
            ? new Moment(releasedAtValue)
            : (Moment?)null;
        return new AppStoreProbeResult(versionString, releasedAt);
    }

    // It's internal to be accessible from tests.
    // Google Play has no public JSON API, so this reads the store page and pulls the
    // "About this app - Version" data block, which carries the full versionName.
    internal static AppStoreProbeResult ParseGoogle(string body)
    {
        // Every other X.Y.Z on the page is review metadata, so only the [[["..."]]] shape counts,
        // and anything but exactly one match is a page change we must not read as a version.
        var matches = VersionBlockRegex().Matches(body);
        if (matches.Count != 1)
            throw StandardError.Format($"The Play store page has {matches.Count} version blocks, expected 1.");

        return new AppStoreProbeResult(matches[0].Groups[1].Value, null);
    }

    // It's internal to be accessible from tests.
    // The MSIX package name carries the nbgv version with a trailing ".0",
    // so the highest package version is directly comparable.
    internal static AppStoreProbeResult ParseMicrosoft(string body)
    {
        using var json = JsonDocument.Parse(body);
        if (!json.RootElement.TryGetProperty("Products", out var products)
            || products.ValueKind != JsonValueKind.Array)
            throw StandardError.Format("The DisplayCatalog response has no 'Products' array.");
        if (products.GetArrayLength() == 0)
            throw StandardError.Format("The app isn't sold in this storefront.");

        var lastVersionString = "";
        var lastVersion = VersionExt.Zero;
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
                        || !VersionExt.TryParseBuildVersion(match.Groups[1].Value, out var version)
                        || version <= lastVersion)
                        continue;

                    lastVersionString = match.Groups[1].Value;
                    lastVersion = version;
                    releasedAt = sku.TryGetProperty("LastModifiedDate", out var lastModifiedAt)
                        && lastModifiedAt.TryGetDateTimeOffset(out var lastModifiedAtValue)
                        ? new Moment(lastModifiedAtValue)
                        : null;
                }
            }
        }

        return new AppStoreProbeResult(lastVersionString, releasedAt); // Throws if lastVersionString == ""
    }

    // It's internal to be accessible from tests.
    // The App Store lookup is served by Akamai with a ~24h max-age, and
    // "Cache-Control: no-cache" doesn't make Akamai revalidate, so we add a cache buster.
    internal static Uri AddCacheBuster(Uri uri)
    {
        var rnd = Alphabet.AlphaNumeric.Generator8.Next();
        return new($"{uri.AbsoluteUri}{(uri.Query.IsNullOrEmpty() ? '?' : '&')}_={rnd}");
    }

    // Private methods

    private async Task<string> Fetch(Uri uri, CancellationToken cancellationToken)
    {
        // Returns null when the store says the app isn't there; every other failure throws
        using var client = HttpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, AddCacheBuster(uri));
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgentValue);
        request.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
        using var response = await client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw StandardError.Constraint($"{uri.Host} returned {response.StatusCode} status code.");

        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxResponseSize)
            throw StandardError.Constraint($"{uri.Host} returned more than {MaxResponseSize} bytes.");

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var _ = stream.ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        var buffer = new char[16 * 1024];
        var sb = ActualLab.Text.StringBuilderExt.Acquire();
        while (true) {
            var readCount = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (readCount == 0)
                break;

            sb.Append(buffer, 0, readCount);
            if (sb.Length > MaxResponseSize)
                throw StandardError.Constraint($"{uri.Host} returned more than {MaxResponseSize} bytes.");
        }

        return sb.ToStringAndRelease();
    }

    [GeneratedRegex(@"\[\[\[""(\d+\.\d+\.\d+)""\]\]")]
    private static partial Regex VersionBlockRegex();

    [GeneratedRegex(@"_(\d+\.\d+\.\d+(?:\.\d+)?)_")]
    private static partial Regex PackageVersionRegex();
}
