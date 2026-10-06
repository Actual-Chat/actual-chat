using System.Collections.Specialized;
using System.Text.RegularExpressions;
using System.Web;

namespace ActualChat.Media;

/// <summary>
/// Extracts the point a map link shows - Google, Apple, OpenStreetMap, Yandex, Bing, Waze and 2GIS links
/// that carry coordinates. A link naming a place without them (a place id, a search query) has no target.
/// Google and Apple links may also name the point they show.
/// </summary>
public static partial class MapLinkParser
{
    private static readonly string[] GoogleQueryKeys = ["q", "query", "ll", "destination", "daddr", "center"];
    private static readonly string[] AppleQueryKeys = ["coordinate", "ll", "q", "daddr", "sll", "center"];
    private static readonly string[] WazeQueryKeys = ["ll", "latlng", "to"];
    // "ll" is the center of the map, which is only near the place the link shows
    private static readonly string[] YandexQueryKeys = ["pt", "poi[point]", "whatshere[point]", "ll"];

    [GeneratedRegex(@"!3d(?<lat>-?\d+(\.\d+)?)!4d(?<lng>-?\d+(\.\d+)?)", RegexOptions.ExplicitCapture)]
    private static partial Regex GooglePinRegexFactory();

    [GeneratedRegex(
        @"/(@|place/|search/)(?<lat>-?\d+(\.\d+)?),\+?(?<lng>-?\d+(\.\d+)?)",
        RegexOptions.ExplicitCapture)]
    private static partial Regex GooglePathRegexFactory();

    [GeneratedRegex(@"/place/(?<name>[^/@]+)", RegexOptions.ExplicitCapture)]
    private static partial Regex GooglePlaceRegexFactory();

    [GeneratedRegex(@"map=\d+(\.\d+)?/(?<lat>-?\d+(\.\d+)?)/(?<lng>-?\d+(\.\d+)?)", RegexOptions.ExplicitCapture)]
    private static partial Regex OpenStreetMapFragmentRegexFactory();

    [GeneratedRegex(@"/geo/(?<lng>-?\d+(\.\d+)?),(?<lat>-?\d+(\.\d+)?)", RegexOptions.ExplicitCapture)]
    private static partial Regex TwoGisPathRegexFactory();

    public static MapLinkTarget? TryParse(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !EgressHttpHandler.IsHttpUri(uri))
            return null;

        var host = uri.DnsSafeHost.ToLower();
        if (host.StartsWith("www."))
            host = host[4..];
        var path = Uri.UnescapeDataString(uri.AbsolutePath);
        var query = HttpUtility.ParseQueryString(uri.Query);
        var labels = host.Split('.');

        // Google asks for cookie consent in some regions, and the link itself goes into "continue"
        if (host == "consent.google.com")
            return query["continue"] is { } continueUrl && !continueUrl.Contains("consent.google.com")
                ? TryParse(continueUrl)
                : null;
        if (labels.Contains("google") && (labels[0] == "maps" || path.StartsWith("/maps"))) {
            var point = FromMatch(GooglePinRegexFactory().Match(path))
                ?? FromQuery(query, GoogleQueryKeys)
                ?? FromMatch(GooglePathRegexFactory().Match(path));
            var name = GooglePlaceRegexFactory().Match(path).Groups["name"].Value.Replace('+', ' ');
            return ToTarget(point, name);
        }

        if (host == "maps.apple.com")
            return ToTarget(FromQuery(query, AppleQueryKeys), query["name"], query["q"]);
        if (host is "openstreetmap.org" or "osm.org")
            return ToTarget(GeoPoint.TryParse($"{query["mlat"]},{query["mlon"]}")
                ?? FromMatch(OpenStreetMapFragmentRegexFactory().Match(uri.Fragment)));
        if (labels.Contains("yandex") && (labels[0] == "maps" || path.StartsWith("/maps")))
            return ToTarget(YandexQueryKeys
                .Select(x => FromLongitudeFirst(query[x]))
                .FirstOrDefault(x => x is not null));
        if (host == "bing.com" && path.StartsWith("/maps"))
            return ToTarget(GeoPoint.TryParse(query["cp"]?.Replace('~', ',')));
        if (host is "waze.com" or "ul.waze.com")
            return ToTarget(FromQuery(query, WazeQueryKeys));
        if (labels[0] == "2gis")
            return ToTarget(FromLongitudeFirst(query["m"]) ?? FromMatch(TwoGisPathRegexFactory().Match(path)));

        return null;
    }

    // Private methods

    private static MapLinkTarget? ToTarget(GeoPoint? point, params ReadOnlySpan<string?> nameCandidates)
    {
        if (point is null)
            return null;

        foreach (var candidate in nameCandidates) {
            // A dropped pin has its coordinates where the name goes: "48.85837,2.294481" or 48°51'30.1"N 2°17'40.1"E
            var name = (candidate ?? "").Trim();
            var isName = !name.IsNullOrEmpty()
                && !name.Contains('°')
                && GeoPoint.TryParse(name.Replace("loc:", "")) is null;
            if (isName)
                return new MapLinkTarget(point, name);
        }

        return new MapLinkTarget(point);
    }

    private static GeoPoint? FromQuery(NameValueCollection query, string[] keys)
    {
        foreach (var key in keys) {
            // "loc:" and "ll." are the prefixes Google and Waze put before the pair
            var value = query[key]?.Replace("loc:", "").Replace("ll.", "");
            if (GeoPoint.TryParse(value) is { } point)
                return point;
        }

        return null;
    }

    private static GeoPoint? FromLongitudeFirst(string? value)
    {
        // Yandex and 2GIS write "longitude,latitude", followed by a marker style or a zoom
        var parts = (value ?? "").Split(',', '/', '~');
        return parts.Length >= 2 ? GeoPoint.TryParse($"{parts[1]},{parts[0]}") : null;
    }

    private static GeoPoint? FromMatch(Match match)
        => match.Success ? GeoPoint.TryParse($"{match.Groups["lat"].Value},{match.Groups["lng"].Value}") : null;
}
