using ActualChat.Rpc;

namespace ActualChat.App.Maui.Services;

/// <summary>
/// Sends a request for our own content to the content host RPC uses, which may be an edge node's.
/// Only the outgoing request moves: the caller's URL, and a cache key built from it, keep the origin host.
/// </summary>
public sealed class ContentHostHandler(UrlMapper urlMapper) : DelegatingHandler
{
    private UrlMapper UrlMapper { get; } = urlMapper;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is { } uri
            && UrlMapper.IsOwnContentUrl(uri.AbsoluteUri)
            && RpcEndpointSelector.IsContentRemapped(uri.AbsoluteUri, out var contentUrl)) {
            request.RequestUri = new Uri(contentUrl);
        }
        return base.SendAsync(request, cancellationToken);
    }
}
