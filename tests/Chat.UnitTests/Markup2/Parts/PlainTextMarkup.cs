namespace ActualChat.Chat.UnitTests.Markup2;

public record PlainTextMarkup(string Text) : TextMarkup
{
    public PlainTextMarkup() : this("") { }

    public override string ToPlainText()
        => Text;
}
