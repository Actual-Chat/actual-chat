using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ActualChat.Chat.UnitTests.Markup2;

public sealed record UrlMarkup(string Url) : TextMarkup
{
    private static readonly Regex ImageUrlRegex = new(
        "\\.(jpg|jpeg|png|gif|png|webp)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsImage => ImageUrlRegex.IsMatch(Url);

    public UrlMarkup() : this("") { }

    public override string ToMarkupText()
        => Url;
}
