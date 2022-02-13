using System.Text.RegularExpressions;

namespace ActualChat.Chat.Sanitizer;

// HtmlSanitizer is taken from https://github.com/mganss/HtmlSanitizer
internal class HtmlSanitizer
{
    private static readonly Regex SchemeRegex
        = new(@"^\s*([^\/#]*?)(?:\:|&#0*58|&#x0*3a)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static ISet<string> DefaultAllowedSchemes { get; }
        = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "http", "https", "tel", "mailto" };

    public ISet<string> AllowedSchemes { get; }

    public HtmlSanitizer(IEnumerable<string>? allowedSchemes = null)
    {
        AllowedSchemes = new HashSet<string>(allowedSchemes ?? DefaultAllowedSchemes, StringComparer.OrdinalIgnoreCase);
    }

    public virtual string? SanitizeUrl(string url, string baseUrl)
    {
        var iri = GetSafeIri(url);

        if (iri != null && !iri.IsAbsolute && !string.IsNullOrEmpty(baseUrl)) {
            // resolve relative uri
            if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)) {
                try {
                    return new Uri(baseUri, iri.Value).AbsoluteUri;
                }
                catch (UriFormatException) {
                    iri = null;
                }
            }
            else iri = null;
        }

        var sanitizedUrl = iri?.Value;
        return sanitizedUrl;
    }

    protected Iri? GetSafeIri(string url)
    {
        var schemeMatch = SchemeRegex.Match(url);

        if (schemeMatch.Success) {
            var scheme = schemeMatch.Groups[1].Value;
            return AllowedSchemes.Contains(scheme, StringComparer.OrdinalIgnoreCase) ? new Iri(url, scheme) : null;
        }

        return new Iri(url);
    }
}
