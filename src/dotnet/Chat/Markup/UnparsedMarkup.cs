namespace ActualChat.Chat;

public sealed record UnparsedMarkup(string Text) : PlainTextMarkup(Text)
{
    public UnparsedMarkup() : this("") { }
}
