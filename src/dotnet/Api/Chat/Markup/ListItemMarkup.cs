using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed record ListItemMarkup(Markup Content, bool Ordered, int? Order = null) : Markup
{
    public override string Format()
        => GetPrefix() + Content.Format();

    private string GetPrefix()
        => Ordered ? $"{Order}. " : "- ";

    // This record relies on referential equality
    public bool Equals(ListItemMarkup? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
