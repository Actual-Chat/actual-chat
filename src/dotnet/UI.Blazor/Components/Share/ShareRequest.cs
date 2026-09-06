namespace ActualChat.UI.Blazor.Components;

public sealed record ShareRequest(
    string Text,
    LocalUrl? Link = null)
{
    public IReadOnlyList<MediaRef> Media { get; init; } = [];

    // An absolute, off-origin URL with no LocalUrl form (e.g. a link preview's external target)
    public string? ExternalLink { get; init; }

    public ShareRequest(LocalUrl link) : this("", link)
    { }

    public bool HasLink()
        => Link.HasValue;
    public bool HasLink(out LocalUrl link)
    {
        if (Link is { } vLink) {
            link = vLink;
            return true;
        }
        link = default;
        return false;
    }

    public bool HasExternalLink()
        => !ExternalLink.IsNullOrEmpty();
    public bool HasAnyLink()
        => HasLink() || HasExternalLink();

    public bool HasMedia()
        => Media.Count > 0;
}
