namespace ActualChat.Chat.UnitTests.Markup2;

public sealed record PreformattedTextMarkup(string Text) : TextMarkup
{
    public PreformattedTextMarkup() : this("") { }

    public override string ToPlainText()
        => $"`{Text.Replace("`", "``")}`";
}
