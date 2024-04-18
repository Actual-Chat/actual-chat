using HtmlAgilityPack;

namespace ActualChat.Media;

public static class OpenGraphParser
{
    public static OpenGraph? Parse(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var head = doc.DocumentNode.ChildNodes["html"]?.ChildNodes["head"];
        if (head is null)
            return null;

        var metaMap = head.ChildNodes.Where(x => OrdinalIgnoreCaseEquals(x.Name, "meta"))
            .Select(x => KeyValuePair.Create(x.GetAttributeValue("property", ""), x.GetAttributeValue("content", "")))
            .Where(x => !x.Key.IsNullOrEmpty() && !x.Value.IsNullOrEmpty())
            .Distinct()
            .ToDictionary(StringComparer.OrdinalIgnoreCase);
        var title = metaMap.GetValueOrDefault("og:title").NullIfEmpty() ?? head.ChildNodes["title"]?.InnerText;
        if (title.IsNullOrEmpty())
            return null;

        return new OpenGraph(title.HtmlDecode()) {
            Description = metaMap.GetValueOrDefault("og:description", "").HtmlDecode(),
            ImageUrl = GetUrl(metaMap, "og:image"),
            SiteName = metaMap.GetValueOrDefault("og:site_name", "").HtmlDecode(),
            Video = new OpenGraphVideo {
                SecureUrl = GetUrl(metaMap, "og:video:secure_url"),
                Height = GetInt(metaMap, "og:video:height") ?? 0,
                Width = GetInt(metaMap, "og:video:width") ?? 0,
            },
        };
    }

    private static string GetUrl(Dictionary<string, string> metaMap, string key)
        => Uri.TryCreate(metaMap.GetValueOrDefault(key), UriKind.Absolute, out var uri) ? uri.AbsoluteUri : "";

    private static int? GetInt(Dictionary<string, string> metaMap, string key)
        => int.TryParse(metaMap.GetValueOrDefault(key), CultureInfo.InvariantCulture, out var i) ? i : null;
}
