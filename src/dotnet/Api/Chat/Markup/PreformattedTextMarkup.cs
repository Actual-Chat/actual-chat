using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed record PreformattedTextMarkup(string Text) : TextMarkup(Text)
{
    public static new readonly PreformattedTextMarkup Empty = new("");

    public override TextMarkupKind Kind => TextMarkupKind.Preformatted;

    public PreformattedTextMarkup() : this("") { }

    public override string Format()
        => $"`{Text.OrdinalReplace("`", "``")}`";

    // This record relies on referential equality
    public bool Equals(PreformattedTextMarkup? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
