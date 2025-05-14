using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed record NewLineMarkup() : TextMarkup("\r\n")
{
    public static readonly NewLineMarkup Instance = new();

    public override TextMarkupKind Kind => TextMarkupKind.NewLine;

    // This record relies on referential equality
    public bool Equals(NewLineMarkup? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
