namespace ActualChat.Chat;

public enum TextStyle { None = 0, Italic = 1, Bold = 2 }

public sealed record StylizedMarkup(Markup Content, TextStyle Style) : Markup
{
    public string StyleToken => Style switch {
        TextStyle.None => "",
        TextStyle.Italic => "*",
        TextStyle.Bold => "**",
        _ => throw StandardError.Internal($"Invalid {nameof(Style)} property value."),
    };

    public StylizedMarkup() : this(null!, default) { }

    public override string Format()
    {
        var markupText = Content.Format();
        return $"{StyleToken}{markupText}{StyleToken}";
    }

    public override Markup Simplify()
    {
        var markup = Content.Simplify();
        return ReferenceEquals(markup, Content) ? this : new StylizedMarkup(markup, Style);
    }
}
