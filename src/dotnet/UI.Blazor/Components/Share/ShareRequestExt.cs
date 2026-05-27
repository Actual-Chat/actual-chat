namespace ActualChat.UI.Blazor.Components;

public static class ShareRequestExt
{
    public static string GetDisplayTextAndLink(this ShareRequest request)
    {
        if (request.Link is not { } link)
            return request.Text;
        var displayLink = GetDisplayLink(link);
        return request.Text.IsNullOrEmpty() ? displayLink : $"{request.Text}: {displayLink}";
    }

    public static string? GetDisplayLink(this ShareRequest request)
        => request.Link is { } link ? GetDisplayLink(link) : null;

    public static string GetShareTextAndLink(this ShareRequest request, UrlMapper urlMapper)
    {
        if (request.Link is not { } link)
            return request.Text;
        var fullLink = urlMapper.ToAbsolute(link);
        return request.Text.IsNullOrEmpty() ? fullLink : $"{request.Text}: {fullLink}";
    }

    public static string GetShareLink(this ShareRequest request, UrlMapper urlMapper)
        => request.Link is { } link ? urlMapper.ToAbsolute(link) : "";

    private static string GetDisplayLink(LocalUrl link)
        => link.IsHome() ? link.Value : link.Value[1..];
}
