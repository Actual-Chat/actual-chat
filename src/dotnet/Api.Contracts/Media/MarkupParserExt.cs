namespace ActualChat.Media;

public static class MarkupParserExt
{
    private static readonly HashSet<string> SupportedSchemes = new (StringComparer.OrdinalIgnoreCase) { "http", "https" };

    public static IEnumerable<string> ExtractLinks(this IMarkupParser markupParser, string content, int? limit = null)
    {
        if (content.IsNullOrEmpty())
            return [];

        var markup = markupParser.Parse(content);
        var links = new LinkExtractor().GetLinks(markup).Where(IsSupportedUrl);
        return limit != null ? links.Take(limit.Value) : links;

        bool IsSupportedUrl(string x)
            => Uri.TryCreate(x, UriKind.Absolute, out var uri) && SupportedSchemes.Contains(uri.Scheme);
    }

    // Skips trusted-GIF hosts: those URLs render as inline <img> via image proxy
    // (UrlMarkupView), not as link previews — generating previews for them would be
    // pointless.
    public static Symbol[] ExtractLinkPreviewIds(this IMarkupParser markupParser, ChatEntry entry)
        => markupParser.ExtractLinks(entry.Content)
            .Where(x => !UrlMapper.IsTrustedGifUrl(x))
            .Take(Constants.Media.LinkPreviewsPerMessageLimit)
            .Select(LinkPreview.ComposeId)
            .ToArray();
}
