using System.Text;

namespace ActualChat.Chat;

public abstract record TextMarkup(string Text) : Markup
{
    public override string ToMarkupText()
        => Text;

    protected override bool PrintMembers(StringBuilder builder)
    {
        builder.Append(Invariant($"{nameof(Text)} = \"{Text.Replace("\"", "\\\"", StringComparison.InvariantCulture)}\""));
        return true; // Indicates there is no comma / tail "}" must be prefixed with space
    }
}

public record PlainTextMarkup(string Text) : TextMarkup(Text)
{
    public PlainTextMarkup() : this("") { }

    public override string ToMarkupText()
        => Text;

    protected override bool PrintMembers(StringBuilder builder)
    {
        builder.Append(Invariant($"{nameof(Text)} = \"{Text.Replace("\"", "\\\"", StringComparison.InvariantCulture)}\""));
        return true; // Indicates there is no comma / tail "}" must be prefixed with space
    }
}
