namespace ActualChat.UI.Blazor.Components;

public sealed record ShareRequest(
    string Text,
    LocalUrl? Link = null)
{
    public IReadOnlyList<ChatEntryAttachment> Attachments { get; init; } = [];

    public ShareRequest(LocalUrl link) : this("", link)
    { }

    public bool HasText()
        => !Text.IsNullOrEmpty();

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

    public bool HasAttachments()
        => Attachments.Count > 0;

    // GetXxx

    public string GetDisplayTextAndLink()
    {
        if (!HasLink())
            return Text;

        if (Text.IsNullOrEmpty())
            return GetDisplayLink() ?? "";

        return $"{Text}: {GetDisplayLink()}";
    }

    public string? GetDisplayLink()
    {
        if (!HasLink(out var link))
            return null;

        return link.IsHome() ? link.Value : link.Value[1..];
    }

    public string GetShareTextAndLink(UrlMapper urlMapper)
    {
        if (!HasLink())
            return Text;

        if (Text.IsNullOrEmpty())
            return GetShareLink(urlMapper) ?? "";

        return $"{Text}: {GetShareLink(urlMapper)}";
    }

    public string GetShareLink(UrlMapper urlMapper)
        => HasLink(out var link) ? urlMapper.ToAbsolute(link) : "";
}
