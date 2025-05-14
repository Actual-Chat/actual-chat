using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed class UrlMarkup(string url, UrlMarkupKind kind) : Markup
{
    public UrlMarkup() : this("", UrlMarkupKind.Www) { }
    public string Url { get; init; } = url;
    public UrlMarkupKind Kind { get; init; } = kind;

    public override string Format()
        => Url;
}
