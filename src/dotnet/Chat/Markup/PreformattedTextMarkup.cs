namespace ActualChat.Chat;

public sealed record PreformattedTextMarkup(string Text) : TextMarkup
{
    public PreformattedTextMarkup() : this("") { }

    public override string ToMarkupText()
        => $"`{Text.Replace("`", "``", StringComparison.InvariantCulture)}`";
}
