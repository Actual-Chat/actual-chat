namespace ActualChat.Chat;

public sealed record UnparsedTextMarkup(string Text) : TextMarkup(Text)
{
    public static new UnparsedTextMarkup Empty { get; } = new("");

    public override TextMarkupKind Kind => TextMarkupKind.Unparsed;

    public UnparsedTextMarkup() : this("") { }
}
