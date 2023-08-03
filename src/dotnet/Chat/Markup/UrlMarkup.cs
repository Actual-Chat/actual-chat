namespace ActualChat.Chat;

public sealed partial record UrlMarkup(string Url, UrlMarkupKind Kind) : Markup
{
    public UrlMarkup() : this("", UrlMarkupKind.Www) { }

    public override string Format()
        => Url;
}
