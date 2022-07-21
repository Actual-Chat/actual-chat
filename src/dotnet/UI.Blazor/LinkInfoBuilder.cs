using ActualChat.Hosting;

namespace ActualChat.UI.Blazor;

public class LinkInfoBuilder
{
    private readonly NavigationManager _nav;
    private readonly UriMapper _uriMapper;
    private readonly bool _isMaui;

    public LinkInfoBuilder(NavigationManager nav, UriMapper uriMapper, HostInfo hostInfo)
    {
        _nav = nav;
        _uriMapper = uriMapper;
        _isMaui = hostInfo.HostKind == HostKind.Maui;
    }

    public LinkInfo GetFrom(string relativeUri)
    {
        var navigateLink = _nav.ToAbsoluteUri(relativeUri);
        return new LinkInfo(navigateLink, _isMaui ? _uriMapper.ToAbsolute(relativeUri) : navigateLink);
    }
}

public record LinkInfo(Uri NavigateLink, Uri CopyLink)
{
    public Uri DisplayLink => CopyLink;
}
