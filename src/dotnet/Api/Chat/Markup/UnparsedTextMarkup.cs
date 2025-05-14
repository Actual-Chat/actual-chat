using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed record UnparsedTextMarkup(string Text) : TextMarkup(Text)
{
    public static new readonly UnparsedTextMarkup Empty = new("");

    public override TextMarkupKind Kind => TextMarkupKind.Unparsed;

    public UnparsedTextMarkup() : this("") { }

    // This record relies on referential equality
    public bool Equals(UnparsedTextMarkup? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
