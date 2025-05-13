namespace ActualChat.UI.Blazor.Components;

public readonly record struct ShareRequest
{
    [field: AllowNull, MaybeNull]
    public string Text {
        get => field ?? "";
        init;
    }

    public LocalUrl? Link { get; init; }

    public ShareRequest(LocalUrl link)
    {
        Text = "";
        Link = link;
    }

    // ReSharper disable once ConvertToPrimaryConstructor
    public ShareRequest(string text, LocalUrl? link = null)
    {
        Text = text;
        Link = link;
    }

    // WithXxx

    public ShareRequest WithText(string text)
        => new() { Text = text, Link = Link };

    public ShareRequest WithTextUnlessEmpty(string text)
        => text.IsNullOrEmpty() ? this : new() { Text = text, Link = Link };

    // HasXxx

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

    // Equality
    public bool Equals(ShareRequest other)
        => OrdinalEquals(Text, other.Text) && Link == other.Link;
    public override int GetHashCode()
        => HashCode.Combine(Text, Link);
}
