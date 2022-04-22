namespace ActualChat.Chat.UnitTests.Markup2;

public sealed record CodeBlockMarkup(
    string Code,
    string Language = ""
    ) : Markup
{
    public CodeBlockMarkup() : this("") { }

    public override string ToPlainText()
        => $"```{Language}\r\n{Code}```";
}
