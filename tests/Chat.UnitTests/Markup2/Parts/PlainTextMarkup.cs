using System.Text;

namespace ActualChat.Chat.UnitTests.Markup2;

public record PlainTextMarkup(string Text) : TextMarkup
{
    public PlainTextMarkup() : this("") { }

    public override string ToPlainText()
        => Text;

    protected override bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"{nameof(Text)} = \"{Text.Replace("\"", "\\\"")}\"");
        return false;
    }
}
