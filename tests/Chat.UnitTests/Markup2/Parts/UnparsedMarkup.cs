namespace ActualChat.Chat.UnitTests.Markup2;

public sealed record UnparsedMarkup(string Text) : PlainTextMarkup(Text)
{
    public UnparsedMarkup() : this("") { }
}
