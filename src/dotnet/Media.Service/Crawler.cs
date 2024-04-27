using System.Net.Http.Headers;
using TurnerSoftware.RobotsExclusionTools;

namespace ActualChat.Media;

public sealed class Crawler(
    IHttpClientFactory httpClientFactory,
    IEnumerable<ICrawlingHandler> handlers,
    ILogger<Crawler> log)
{
    public const string HttpClientName = nameof(Crawler);
    public static readonly string DefaultUserAgent = new ($"ActualChat-Bot/{ThisAssembly.AssemblyVersion}");
    private static readonly string[] UserAgents = [
        DefaultUserAgent,
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36 Edg/124.0.0.0",
    ];

    private HttpClient HttpClient { get; } = httpClientFactory.CreateClient(HttpClientName);
    private RobotsFileParser RobotsParser { get; } = new (httpClientFactory.CreateClient(HttpClientName));

    private IReadOnlyList<ICrawlingHandler> Handlers { get; } = handlers.ToList();

    public async Task<CrawledLink> Crawl(string url, CancellationToken cancellationToken)
    {
        var userAgents = await ListSupportedUserAgents(new Uri(url), cancellationToken).ConfigureAwait(false);
        if (userAgents.Count == 0)
            return CrawledLink.None;

        var response = await SendRequest(url, userAgents, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return CrawledLink.None;

        var handler = Handlers.FirstOrDefault(x => x.Supports(response));
        if (handler is null)
            return CrawledLink.None;

        return await handler.Handle(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendRequest(string url, IReadOnlyCollection<string> userAgents, CancellationToken cancellationToken)
    {
        HttpResponseMessage response = null!;
        foreach (var userAgent in userAgents) {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(userAgent);
            request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
            response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            log.LogDebug("{ResponseCode}: {Url}. {UserAgent}", response.StatusCode, url, userAgent);
            if (response.IsSuccessStatusCode)
                return response;
        }
        return response;
    }

    private async Task<IReadOnlyCollection<string>> ListSupportedUserAgents(Uri uri, CancellationToken cancellationToken)
    {
        // TODO: cache robots.txt
        var robotsUri = new Uri(uri, "/robots.txt");
        var robotsFile = await RobotsParser.FromUriAsync(robotsUri, cancellationToken).ConfigureAwait(false);
        return UserAgents.Where(x => robotsFile.IsAllowedAccess(uri, x)).ToList();
    }
}
