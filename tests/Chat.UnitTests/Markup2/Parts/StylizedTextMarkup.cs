namespace ActualChat.Chat.UnitTests.Markup2;

public enum TextStyle { Italic = 1, Bold = 2 }

public sealed record StylizedTextMarkup(TextMarkup Markup, TextStyle Style) : TextMarkup
{
    public StylizedTextMarkup() : this(null!, 0) { }

    public override string ToPlainText()
        => Style switch {
            0 => Markup.ToPlainText(),
            TextStyle.Italic => $"*{Markup.ToPlainText()}*",
            TextStyle.Bold => $"**{Markup.ToPlainText()}**",
            _ => throw new ArgumentOutOfRangeException(nameof(Style)),
        };
}
