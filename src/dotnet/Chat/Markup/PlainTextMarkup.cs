using System.Text;

namespace ActualChat.Chat;

public record PlainTextMarkup(string Text) : TextMarkup(Text)
{
    public static new PlainTextMarkup Empty { get; } = new("");

    public override TextMarkupKind Kind => TextMarkupKind.Plain;

    public PlainTextMarkup() : this("") { }

    public override string Format()
        => Text;

    protected override bool PrintMembers(StringBuilder builder)
    {
        builder.Append(Invariant($"{nameof(Text)} = \"{Text.OrdinalReplace("\"", "\\\"")}\""));
        return true; // Indicates there is no comma / tail "}" must be prefixed with space
    }
}
