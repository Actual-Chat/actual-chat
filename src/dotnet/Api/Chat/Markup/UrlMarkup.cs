using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed record UrlMarkup(string Url, UrlMarkupKind Kind) : Markup
{
    public UrlMarkup() : this("", UrlMarkupKind.Www) { }

    public override string Format()
        => Url;

    // This record relies on referential equality
    public bool Equals(UrlMarkup? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
