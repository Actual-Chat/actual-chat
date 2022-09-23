namespace ActualChat.Chat;

public sealed record PreformattedTextMarkup(string Text) : TextMarkup(Text)
{
    public static new PreformattedTextMarkup Empty { get; } = new("");

    public override TextMarkupKind Kind => TextMarkupKind.Preformatted;

    public PreformattedTextMarkup() : this("") { }

    public override string Format()
        => $"`{Text.OrdinalReplace("`", "``")}`";
}
