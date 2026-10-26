using System.Collections.Specialized;

namespace ActualChat.Users;

/// <summary>
/// Turns what a client captured (a store referrer, a landing URL, Apple's AdServices answer, an arrival)
/// into <see cref="AttributionDetails"/>. Measurement must never fail a flow, so bad input yields null.
/// </summary>
public static class AttributionParser
{
    public const int MaxRawLength = 2048;
    public const int MaxValueLength = 64;
    public const int MaxTermLength = 100;
    private static readonly HashSet<string> ValueTrackMatchTypes = ["e", "p", "b"];

    public static AttributionDetails? Parse(AttributionOrigin origin, string? raw)
    {
        if (raw.IsNullOrEmpty() || raw.Length > MaxRawLength)
            return null;

        var details = origin switch {
            AttributionOrigin.AppleAdServices => ParseAppleAdServices(raw),
            AttributionOrigin.Referral or AttributionOrigin.LegacySignUp => ParseArrival(raw),
            AttributionOrigin.Url or AttributionOrigin.PlayReferrer => ParseQuery(origin, raw),
            _ => null,
        };
        return details is { IsAttributed: true } ? details : null;
    }

    public static NameValueCollection? GetQueryItems(string? urlOrQuery)
    {
        // Accepts a local URL ("/path?a=b"), a bare query ("a=b") or a store install referrer
        if (urlOrQuery.IsNullOrEmpty())
            return null;

        var queryStart = urlOrQuery.IndexOf('?');
        var query = queryStart >= 0 ? urlOrQuery[(queryStart + 1)..] : urlOrQuery;
        var fragmentStart = query.IndexOf('#');
        if (fragmentStart >= 0)
            query = query[..fragmentStart];
        return query.Contains('=') ? UriExt.GetQueryCollection(query) : null;
    }

    // Private methods

    private static AttributionDetails? ParseQuery(AttributionOrigin origin, string raw)
    {
        if (GetQueryItems(raw) is not { } items)
            return null;

        var (clickIdKind, clickId) = items["gclid"] is { Length: > 0 } gclid ? (ClickIdKind.Gclid, gclid)
            : items["gbraid"] is { Length: > 0 } gbraid ? (ClickIdKind.Gbraid, gbraid)
            : items["wbraid"] is { Length: > 0 } wbraid ? (ClickIdKind.Wbraid, wbraid)
            : ((ClickIdKind?)null, "");
        var source = Clean(items["utm_source"]).ToLower();
        var content = Clean(items["utm_content"]);
        var isContentMatchType = ValueTrackMatchTypes.Contains(content);
        var platform = clickIdKind is not null ? "google"
            : !source.IsNullOrEmpty() ? source
            : origin == AttributionOrigin.PlayReferrer ? "play"
            : "web";
        return new AttributionDetails {
            Platform = platform,
            Source = source,
            Medium = Clean(items["utm_medium"]),
            Campaign = Clean(items["utm_campaign"] ?? items["c"]),
            AdGroup = Clean(items["utm_adgroup"]).NullIfEmpty() ?? (isContentMatchType ? "" : content),
            Term = Clean(items["utm_term"], MaxTermLength),
            MatchType = Clean(items["utm_matchtype"]).NullIfEmpty() ?? (isContentMatchType ? content : ""),
            ClickIdKind = clickIdKind,
            ClickId = Clean(clickId, MaxRawLength / 2),
        };
    }

    private static AttributionDetails? ParseAppleAdServices(string raw)
    {
        try {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("attribution", out var attribution)
                || attribution.ValueKind != JsonValueKind.True)
                return null;

            return new AttributionDetails {
                Platform = "apple",
                Source = "apple_ads",
                Campaign = GetValue(root, "campaignId"),
                AdGroup = GetValue(root, "adGroupId"),
                KeywordId = GetValue(root, "keywordId"),
                AdId = GetValue(root, "adId"),
                Claim = GetValue(root, "claimType"),
                InstallKind = GetValue(root, "conversionType"),
                Country = GetValue(root, "countryOrRegion"),
            };
        }
        catch (JsonException) {
            return null;
        }
    }

    private static AttributionDetails? ParseArrival(string raw)
    {
        if (!ArrivalInfo.TryParse(raw, out var arrival))
            return null;

        return arrival.Kind switch {
            ArrivalKind.Join or ArrivalKind.User => new AttributionDetails {
                Platform = "referral",
                Source = arrival.Kind == ArrivalKind.Join ? "join" : "user",
                Campaign = arrival.Id,
            },
            // Apple campaigns arrive as asa-<campaignId>, the AdServices answer as the bare id
            ArrivalKind.Campaign when arrival.Id.StartsWith("asa-") => new AttributionDetails {
                Platform = "apple",
                Source = "apple_ads",
                Campaign = arrival.Id[4..],
            },
            ArrivalKind.Campaign => new AttributionDetails { Campaign = arrival.Id },
            _ => null,
        };
    }

    private static string GetValue(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
            return "";

        return value.ValueKind switch {
            JsonValueKind.Number or JsonValueKind.String => Clean(value.ToString()),
            _ => "",
        };
    }

    private static string Clean(string? value, int maxLength = MaxValueLength)
    {
        if (value.IsNullOrEmpty())
            return "";

        var result = new string(value.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return result.Length > maxLength ? result[..maxLength] : result;
    }
}
