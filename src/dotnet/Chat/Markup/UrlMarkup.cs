using System.Text.RegularExpressions;

namespace ActualChat.Chat;

public sealed record UrlMarkup(string Url) : Markup
{
    private static readonly Regex ImageUrlRegex = new(
        "\\.(jpg|jpeg|png|gif|png|webp)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture);

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsImage => ImageUrlRegex.IsMatch(Url);

    public UrlMarkup() : this("") { }

    public override string Format()
        => Url;
}
