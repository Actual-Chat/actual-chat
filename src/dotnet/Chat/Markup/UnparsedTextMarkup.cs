namespace ActualChat.Chat;

public sealed record UnparsedTextMarkup(string Text) : TextMarkup(Text)
{
    public static new readonly UnparsedTextMarkup Empty = new("");

    public override TextMarkupKind Kind => TextMarkupKind.Unparsed;

    public UnparsedTextMarkup() : this("") { }
}
