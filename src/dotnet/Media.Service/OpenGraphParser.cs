using HtmlAgilityPack;

namespace ActualChat.Media;

public static class OpenGraphParser
{
    public static OpenGraph? Parse(string html, Uri? requestUri)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var head = doc.DocumentNode.ChildNodes["html"]?.ChildNodes["head"];
        if (head is null)
            return null;

        var props = head.ChildNodes.Where(x => OrdinalIgnoreCaseEquals(x.Name, "meta"))
            .Select(x => KeyValuePair.Create(x.GetAttributeValue("property", ""), x.GetAttributeValue("content", "")))
            .Where(x => !x.Key.IsNullOrEmpty() && !x.Value.IsNullOrEmpty());
        var metaMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in props)
            if (!metaMap.ContainsKey(prop.Key))
                metaMap.Add(prop.Key, prop.Value);
        var title = metaMap.GetValueOrDefault("og:title").NullIfEmpty() ?? head.ChildNodes["title"]?.InnerText;
        if (title.IsNullOrEmpty())
            return null;

        var urlExtractor = new UrlExtractor(requestUri);
        return new OpenGraph(title.HtmlDecode()) {
            Description = metaMap.GetValueOrDefault("og:description", "").HtmlDecode(),
            ImageUrl = urlExtractor.GetUrl(metaMap, "og:image:secure_url", "og:image:url", "og:image"),
            SiteName = metaMap.GetValueOrDefault("og:site_name", "").HtmlDecode(),
            Video = new OpenGraphVideo {
                SecureUrl = urlExtractor.GetUrl(metaMap, "og:video:secure_url", "og:video:url"),
                Height = GetInt(metaMap, "og:video:height") ?? 0,
                Width = GetInt(metaMap, "og:video:width") ?? 0,
            },
        };
    }

    private static int? GetInt(Dictionary<string, string> metaMap, string key)
        => int.TryParse(metaMap.GetValueOrDefault(key)?.HtmlDecode(), CultureInfo.InvariantCulture, out var i) ? i : null;

    private struct UrlExtractor(Uri? requestUri)
    {
        private Uri? _baseUrl;

        private Uri? BaseUrl => _baseUrl ??= requestUri != null
            ? new UriBuilder(requestUri.Scheme, requestUri.Host, requestUri.Port).Uri
            : null;

        public string GetUrl(Dictionary<string, string> metaMap, params string[] keys)
        {
            foreach (var key in keys) {
                var url = GetUrl(metaMap, key);
                if (!string.IsNullOrEmpty(url))
                    return url;
            }
            return "";
        }

        public string GetUrl(Dictionary<string, string> metaMap, string key)
        {
            if (!metaMap.TryGetValue(key, out var propValueRaw) || propValueRaw.IsNullOrEmpty())
                return "";

            var propValue = propValueRaw.HtmlDecode();
            if (!Uri.TryCreate(propValue, UriKind.RelativeOrAbsolute, out var uri))
                return "";

            if (uri.IsAbsoluteUri)
                return uri.AbsoluteUri;

            // NOTE(DF): Despite that image/video urls in Open Graph should be absolute urls, there are websites that use relative urls.
            // Try to convert relative urls to absolute ones to get better preview.
            if (BaseUrl is null)
                return "";

            return new Uri(BaseUrl, uri).AbsoluteUri;
        }
    }
}
