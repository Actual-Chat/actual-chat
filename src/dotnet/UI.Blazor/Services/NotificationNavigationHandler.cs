using System.Text.RegularExpressions;

namespace ActualChat.UI.Blazor.Services;

public class NotificationNavigationHandler
{
    private NavigationManager Nav { get; }
    private UrlMapper UrlMapper { get; }

    public NotificationNavigationHandler(NavigationManager nav, UrlMapper urlMapper)
    {
        Nav = nav;
        UrlMapper = urlMapper;
    }

    public Task Handle(string url)
    {
        var origin = UrlMapper.BaseUrl.TrimEnd('/');
        if (url.IsNullOrEmpty() || !url.StartsWith(origin, StringComparison.Ordinal))
            return Task.CompletedTask;

        var chatPageRe = new Regex($"^{Regex.Escape(origin)}/chat/(?<chatid>[a-z0-9-]+)(?:#(?<entryid>)\\d+)?");
        var match = chatPageRe.Match(url);
        if (!match.Success)
            return Task.CompletedTask;

        // Take relative URL to eliminate difference between web app and MAUI app
        var relativeUrl = url[origin.Length..];

        var chatIdGroup = match.Groups["chatid"];
        if (chatIdGroup.Success)
            Nav.NavigateTo(relativeUrl, new NavigationOptions{ ForceLoad = false, ReplaceHistoryEntry = false });
        return Task.CompletedTask;
    }
}
