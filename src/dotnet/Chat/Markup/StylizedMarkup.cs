namespace ActualChat.Chat;

public enum TextStyle { None = 0, Italic = 1, Bold = 2 }

public sealed record StylizedMarkup(Markup Content, TextStyle Style) : Markup
{
    public StylizedMarkup() : this(null!, default) { }

    public override string ToMarkupText()
    {
        var markupText = Content.ToMarkupText();
        var token = GetWrapToken();
        return $"{GetWrapToken()}{markupText}{GetWrapToken()}";
    }

    public string GetWrapToken()
        => Style switch {
            TextStyle.None => "",
            TextStyle.Italic => "*",
            TextStyle.Bold => "**",
            _ => throw new InvalidOperationException($"Invalid {nameof(Style)} property value."),
        };

    public override Markup Simplify()
    {
        var markup = Content.Simplify();
        return ReferenceEquals(markup, Content) ? this : new StylizedMarkup(markup, Style);
    }
}
