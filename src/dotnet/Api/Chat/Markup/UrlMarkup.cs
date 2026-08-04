using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

/// <summary>
/// Represents a URL link in markup.
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MessagePackObject]
public sealed class UrlMarkup(string url, UrlMarkupKind kind) : Markup
{
    public UrlMarkup() : this("", UrlMarkupKind.Www) { }
    [DataMember, Key(0)]
    public string Url { get; init; } = url;
    [DataMember, Key(1)]
    public UrlMarkupKind Kind { get; init; } = kind;

    public override string Format()
        => Url;
}
