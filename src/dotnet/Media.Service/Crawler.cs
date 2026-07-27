using ActualChat.Media.Module;
using ActualLab.Diagnostics;

namespace ActualChat.Media;

/// <summary>
/// Crawls web URLs to extract metadata for link previews.
/// </summary>
public sealed class Crawler(
    IHttpClientFactory httpClientFactory,
    IEnumerable<ICrawlingHandler> handlers,
    MediaSettings settings,
    RobotsFiles robotsFiles,
    ILogger<Crawler> log)
{
    public const string HttpClientName = nameof(Crawler);
    public static readonly string DefaultUserAgent = new ($"ActualChat-Bot/{ThisAssembly.AssemblyVersion}");
    private static readonly string[] UserAgents = [
        DefaultUserAgent,
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36 Edg/124.0.0.0",
    ];

    private ILogger? DebugLog { get; } = log.IfEnabled(LogLevel.Debug, Constants.DebugMode.Crawler);
    private HttpClient HttpClient => field ??= httpClientFactory.CreateClient(HttpClientName);

    private IReadOnlyList<ICrawlingHandler> Handlers => field ??= handlers.ToList();

    public async Task<CrawledLink> Crawl(string url, CancellationToken cancellationToken)
    {
        using var cts = cancellationToken.CreateLinkedTokenSource(settings.CrawlTimeout);
        try {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !EgressHttpHandler.IsHttpUri(uri)) {
                DebugLog?.LogError("Invalid URL: {Url}", url);
                return CrawledLink.None;
            }

            var userAgents = await ListSupportedUserAgents(uri, cts.Token).ConfigureAwait(false);
            if (userAgents.Count == 0)
                return CrawledLink.None;

            var response = await SendRequest(url, userAgents, cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return CrawledLink.None;

            var handler = Handlers.FirstOrDefault(x => x.Supports(response));
            if (handler is null)
                return CrawledLink.None;

            return await handler.Handle(response, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            log.LogWarning("Crawl of '{Url}' timed out after {Timeout}s", url, settings.CrawlTimeout.TotalSeconds);
            throw;
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            var logLevel = e is HttpRequestException ? LogLevel.Debug : LogLevel.Error;
            log.Log(logLevel, e, "Failed to crawl '{Url}'", url);
            return CrawledLink.None;
        }
    }

    private async Task<HttpResponseMessage> SendRequest(string url, IReadOnlyCollection<string> userAgents, CancellationToken cancellationToken)
    {
        HttpResponseMessage response = null!;
        foreach (var userAgent in userAgents) {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(userAgent);
            request.Headers.AcceptLanguage.ParseAdd("*");
            response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            DebugLog?.LogDebug("{ResponseCode}: {Url}. {UserAgent}", response.StatusCode, url, userAgent);
            if (response.IsSuccessStatusCode)
                return response;
        }
        return response;
    }

    private async Task<IReadOnlyCollection<string>> ListSupportedUserAgents(Uri uri, CancellationToken cancellationToken)
    {
        if (settings.DomainsWithoutRobots.Contains(uri.DnsSafeHost.ToLower()))
            return UserAgents;

        var robotsFile = await robotsFiles.Get(uri, cancellationToken).ConfigureAwait(false);
        return UserAgents.Where(x => robotsFile.IsAllowedAccess(uri, x)).ToList();
    }
}
