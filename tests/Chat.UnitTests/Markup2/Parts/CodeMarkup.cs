namespace ActualChat.Chat.UnitTests.Markup2;

public sealed record CodeMarkup(
    string Code,
    string Language = ""
    ) : Markup
{
    public CodeMarkup() : this("") { }

    public override string ToPlainText()
        => $"```{Quote(Code)}```";

    public static string Quote(string text)
        => text.Replace("```", "``````");
}
