namespace ActualChat.Chat;

public sealed record UnparsedTextMarkup(string Text) : TextMarkup(Text)
{
    public UnparsedTextMarkup() : this("") { }
}
