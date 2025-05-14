using System.Text;
using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed record PlainTextMarkup(string Text) : TextMarkup(Text)
{
    public static new readonly PlainTextMarkup Empty = new("");

    public override TextMarkupKind Kind => TextMarkupKind.Plain;

    public PlainTextMarkup() : this("") { }

    public override string Format()
        => Text;

    protected override bool PrintMembers(StringBuilder builder)
    {
        builder.Append(nameof(Text)).Append(" = \"");
        builder.Append(Text.OrdinalReplace("\"", "\\\""));
        builder.Append('"');
        return true; // Indicates there is no comma / tail "}" must be prefixed with space
    }

    // This record relies on referential equality
    public bool Equals(PlainTextMarkup? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
