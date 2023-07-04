using System.Text.RegularExpressions;

namespace ActualChat.Chat;

public sealed partial record UrlMarkup(string Url) : Markup
{
    [GeneratedRegex("\\.(jpg|jpeg|png|gif|png|webp)$", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture)]
    private static partial Regex ImageUrlRegexFactory();

    private static readonly Regex ImageUrlRegex = ImageUrlRegexFactory();

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsImage => ImageUrlRegex.IsMatch(Url);

    public UrlMarkup() : this("") { }

    public override string Format()
        => Url;
}
