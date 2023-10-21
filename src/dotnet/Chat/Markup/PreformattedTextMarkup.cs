namespace ActualChat.Chat;

public sealed record PreformattedTextMarkup(string Text) : TextMarkup(Text)
{
    public static new readonly PreformattedTextMarkup Empty = new("");

    public override TextMarkupKind Kind => TextMarkupKind.Preformatted;

    public PreformattedTextMarkup() : this("") { }

    public override string Format()
        => $"`{Text.OrdinalReplace("`", "``")}`";
}
