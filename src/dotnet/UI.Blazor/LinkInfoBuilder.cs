using ActualChat.Hosting;

namespace ActualChat.UI.Blazor;

public class LinkInfoBuilder
{
    private NavigationManager Nav { get;}
    private UrlMapper UrlMapper { get; }
    private HostInfo HostInfo { get; }

    public LinkInfoBuilder(NavigationManager nav, UrlMapper urlMapper, HostInfo hostInfo)
    {
        Nav = nav;
        UrlMapper = urlMapper;
        HostInfo = hostInfo;
    }

    public LinkInfo GetFrom(string relativeUri)
    {
        var navigateLink = Nav.ToAbsoluteUri(relativeUri).ToString();
        var copyLink = HostInfo.HostKind == HostKind.Maui
            ? UrlMapper.ToAbsolute(relativeUri)
            : navigateLink;
        return new LinkInfo(navigateLink, copyLink);
    }
}

public record LinkInfo(string NavigateLink, string CopyLink)
{
    public string DisplayLink => CopyLink;

    public string ShortDisplayLink => $"{new Uri(CopyLink).Host}{new Uri(CopyLink).AbsolutePath}";
}
