namespace ActualChat.Chat;

public sealed record PreformattedTextMarkup(string Text) : TextMarkup(Text)
{
    public PreformattedTextMarkup() : this("") { }

    public override string ToMarkupText()
        => $"`{Text.Replace("`", "``", StringComparison.InvariantCulture)}`";
}
