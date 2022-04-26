namespace ActualChat.Chat;

public enum TextStyle { None = 0, Italic = 1, Bold = 2 }

public sealed record StylizedMarkup(Markup Markup, TextStyle Style) : TextMarkup
{
    public StylizedMarkup() : this(null!, default) { }

    public override string ToMarkupText()
    {
        var markupText = Markup.ToMarkupText();
        return Style switch {
            0 => markupText,
            TextStyle.Italic => $"*{markupText}*",
            TextStyle.Bold => $"**{markupText}**",
            _ => throw new InvalidOperationException($"Invalid {nameof(Style)} property value."),
        };
    }

    public override Markup Simplify()
    {
        var markup = Markup.Simplify();
        return ReferenceEquals(markup, Markup) ? this : new StylizedMarkup(markup, Style);
    }
}
