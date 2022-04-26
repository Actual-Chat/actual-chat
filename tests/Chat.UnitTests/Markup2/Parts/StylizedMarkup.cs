namespace ActualChat.Chat.UnitTests.Markup2;

public enum TextStyle { Italic = 1, Bold = 2 }

public sealed record StylizedMarkup(Markup Markup, TextStyle Style) : TextMarkup
{
    public StylizedMarkup() : this(null!, 0) { }

    public override string ToMarkupText()
        => Style switch {
            0 => Markup.ToMarkupText(),
            TextStyle.Italic => $"*{Markup.ToMarkupText()}*",
            TextStyle.Bold => $"**{Markup.ToMarkupText()}**",
            _ => throw new ArgumentOutOfRangeException(nameof(Style)),
        };

    public override Markup Simplify()
    {
        var markup = Markup.Simplify();
        return ReferenceEquals(markup, Markup) ? this : new StylizedMarkup(markup, Style);
    }
}
