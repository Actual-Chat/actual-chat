using System.Globalization;
using System.Text;

namespace ActualChat.Media.IntegrationTests;

public class OpenGrapHtmlBuilder
{
    private readonly Dictionary<string, string> _values = new (StringComparer.Ordinal);

    public OpenGrapHtmlBuilder Title(string value)
        => Set("og:title", value);

    public OpenGrapHtmlBuilder Description(string value)
        => Set("og:description", value);

    public OpenGrapHtmlBuilder Image(string value)
        => Set("og:image", value);

    public OpenGrapHtmlBuilder SiteName(string value)
        => Set("og:site_name", value);

    public OpenGrapHtmlBuilder VideoSecureUrl(string value)
        => Set("og:video:secure_url", value);

    public OpenGrapHtmlBuilder VideoHeight(string value)
        => Set("og:video:height", value);

    public OpenGrapHtmlBuilder VideoWidth(string value)
        => Set("og:video:width", value);

    public string Build()
    {
        var sb = new StringBuilder();
        sb.AppendLine("""
                      html prefix="og: https://ogp.me/ns#">
                      <head>
                      <title>Bla bla bla</title>
                      """);
        foreach (var (key, value) in _values)
            sb.AppendLine(CultureInfo.InvariantCulture, $"""<meta property="{key}" content="{value}" />""");
        sb.AppendLine("""
                      </head>
                      <body></body>
                      </html>
                      """);
        return sb.ToString();
    }

    public StringContent BuildHtmlResponseContent()
        => new (Build(), Encoding.UTF8, "text/html");

    private OpenGrapHtmlBuilder Set(string key, string value)
    {
        _values[key] = value;
        return this;
    }
}
