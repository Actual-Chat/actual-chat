using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

/// <summary>
/// Represents a URL link in markup: a bare URL, <c>&lt;url&gt;</c>, or <c>[title](url)</c>.
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
    [DataMember, Key(2)]
    public string? Title { get; init; }
    [DataMember, Key(3)]
    public bool IsEnclosed { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public string DisplayText => Title.NullIfEmpty() ?? Url;

    public override string Format()
        => Title != null ? $"[{Title}]({Url})"
            : IsEnclosed ? $"<{Url}>"
            : Url;
}
