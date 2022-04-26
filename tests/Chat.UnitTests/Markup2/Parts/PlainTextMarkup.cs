using System.Text;

namespace ActualChat.Chat.UnitTests.Markup2;

public record PlainTextMarkup(string Text) : TextMarkup
{
    public PlainTextMarkup() : this("") { }

    public override string ToMarkupText()
        => Text;

    protected override bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"{nameof(Text)} = \"{Text.Replace("\"", "\\\"")}\"");
        return true; // Indicates there is no comma / tail "}" must be prefixed with space
    }
}
