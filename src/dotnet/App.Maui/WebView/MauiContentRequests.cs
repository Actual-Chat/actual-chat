using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using ActualChat.ContentCaching;
using ActualChat.Maui;
using ActualLab.IO;
using Microsoft.Maui.Storage;

namespace ActualChat.App.Maui;

// The WebView evicts immutable media regardless of cache headers, so our own content
// origins (cdn.*, media.*) are served from an encrypted on-disk cache instead.
// A hit is served from disk; a miss streams one download that feeds the WebView and fills the
// cache at once, published when it ends. A duplicate of a running fetch goes back to the WebView.
internal static class MauiContentRequests
{
    // Applies only to responses this cache serves; a bypassed one keeps its own headers.
    // 10s made the WebView re-ask ~8x more often and pushed the CDN into 503s.
    public static readonly TimeSpan ResponseMaxAge = TimeSpan.FromHours(1);

    // Nothing this big belongs in a media cache with no eviction yet
    public const long MaxCachedLength = 250L * 1024 * 1024;

    private static readonly TimeSpan StatsDumpDelay = TimeSpan.FromSeconds(30);
    private static readonly Lock Lock = new();
    private static readonly ConcurrentDictionary<string, byte> Fetches = new();
    private static readonly HttpClient Client = new(new HttpClientHandler {
        // The cache stores the bytes the headers describe, and it never replays a session cookie
        AutomaticDecompression = DecompressionMethods.None,
        UseCookies = false,
        // Intercepting takes the WebView's own connection pool out of the picture, and a
        // screenful of pictures opens this many fills at once
        MaxConnectionsPerServer = 32,
    }) {
        // The response stream outlives the request; cancellation comes from the reader
        Timeout = System.Threading.Timeout.InfiniteTimeSpan,
        // Chromium multiplexes those fills over one HTTP/2 connection; the 1.1 default would
        // serialize them behind the pool instead
        DefaultRequestVersion = HttpVersion.Version20,
        DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        // A WebView serves an intercepted response as-is, so a cached body must be decoded already
        DefaultRequestHeaders = { AcceptEncoding = { new StringWithQualityHeaderValue("identity") } },
    };

    private static FileSystemContentHandler? _cache;
    private static bool _isCacheUnavailable;
    private static long _observedCount;
    private static long _skippedCount;
    private static int _isStarted;

    private static bool DebugMode => CoreConstants.DebugMode.ContentCache;

    private static ILogger Log => field ??= StaticLog.For(typeof(MauiContentRequests));
    private static ILogger? DebugLog => DebugMode ? Log : null;
    private static UrlMapper UrlMapper => field ??= new UrlMapper(MauiSettings.BaseUrl);

    public static void Start()
    {
        if (Interlocked.Exchange(ref _isStarted, 1) != 0)
            return;

        _ = DumpStats();
        return;

        async Task DumpStats() {
            await Task.Delay(StatsDumpDelay).ConfigureAwait(false);
            Log.LogInformation(
                "Content cache stats after {Delay}: {Stats}, Observed={Observed}, Skipped={Skipped}",
                StatsDumpDelay, _cache?.Stats.ToString() ?? "(no cache)",
                Volatile.Read(ref _observedCount), Volatile.Read(ref _skippedCount));
        }
    }

    public static async Task<HttpResponseMessage?> HandleAsync(string? url, string? method, string? range)
    {
        // Hit, else one download that feeds the caller and fills the cache; a duplicate of a
        // fetch already running goes back to the caller, since the first one stores it anyway.
        if (!TryCreateRequest(url, method, range, out var request))
            return null;

        var cache = GetCache();
        if (cache == null)
            return null;

        try {
            var cached = await cache.TryHandleCached(request).ConfigureAwait(false);
            if (cached != null)
                return cached;

            var identity = request.Url.AbsoluteUri;
            if (!Fetches.TryAdd(identity, default))
                return null;

            try {
                return await cache.Handle(request).ConfigureAwait(false);
            }
            finally {
                Fetches.TryRemove(identity, out _);
            }
        }
        catch (Exception e) {
            Log.LogWarning(e, "Content request failed: {Host}{Path}", request.Url.Host, request.Url.AbsolutePath);
            return null;
        }
    }

    // For a native callback that must answer synchronously, on its own thread: reads only what
    // is already on disk, so it never waits on the network. Android gives shouldInterceptRequest
    // ~6 threads, and parking those on a fill stalls every cache hit queued behind them - a
    // screenful of images then lands at once instead of filling in.
    public static HttpResponseMessage? TryHandleCached(string? url, string? method, string? range)
    {
        if (!TryCreateRequest(url, method, range, out var request))
            return null;

        var cache = GetCache();
        if (cache == null)
            return null;

        try {
            return cache.TryHandleCached(request).AsTask().GetAwaiter().GetResult();
        }
        catch (Exception e) {
            Log.LogWarning(e, "Content lookup failed: {Host}{Path}", request.Url.Host, request.Url.AbsolutePath);
            return null;
        }
    }

    public static async Task<HttpResponseMessage?> FetchDirect(string? url, string? method)
    {
        // For a caller with no fallback of its own: on Apple every media URL is projected onto
        // the app's scheme, so one the cache declines still has to be served from somewhere.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;

        try {
            using var message = new HttpRequestMessage(
                method.IsNullOrEmpty() ? HttpMethod.Get : HttpMethod.Parse(method), uri);
            return await Client
                .SendAsync(message, HttpCompletionOption.ResponseHeadersRead)
                .ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Direct content fetch failed: {Host}{Path}", uri.Host, uri.AbsolutePath);
            return null;
        }
    }

    public static (Stream Body, string MimeType)? BeginFetch(string? url, string? method, string? range)
    {
        // One download both feeds the WebView and fills the cache, publishing when it ends.
        // Null means fetch it yourself: a range needs a Content-Range nobody can guess yet.
        if (!range.IsNullOrEmpty() || !TryCreateRequest(url, method, range, out var request))
            return null;

        var cache = GetCache();
        if (cache == null)
            return null;

        var mimeType = MediaMimeTypes.GetMimeType(request.Url.AbsolutePath);
        if (mimeType.IsNullOrEmpty())
            return null;

        // A second request for a resource already being fetched just goes to the WebView: the
        // first one stores it either way, and sharing the download buys nothing (JoinedFill=0
        // in every measurement - Chromium already dedupes its own in-flight loads)
        var identity = request.Url.AbsoluteUri;
        if (!Fetches.TryAdd(identity, default))
            return null;

        var responseTask = Task.Run(async () => {
            try {
                return await cache.Handle(request).ConfigureAwait(false);
            }
            finally {
                Fetches.TryRemove(identity, out _);
            }
        });
        return (new DeferredContentStream(responseTask), mimeType);
    }

    public static void Observe(string? url, string? method)
    {
        if (!TryParse(url, method, out var uri, out var httpMethod))
            return;

        var isCacheable = httpMethod == HttpMethod.Get && IsCacheable(uri);
        if (!isCacheable)
            Interlocked.Increment(ref _skippedCount);
        DebugLog?.LogDebug("Content request: {Method} {Scheme}://{Host}, IsCacheable={IsCacheable}",
            httpMethod, uri.Scheme, uri.Host, isCacheable);
    }

    public static bool IsCacheable(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) && IsCacheable(uri);

    // Private methods

    private static bool TryCreateRequest(
        string? url, string? method, string? range,
        [NotNullWhen(true)] out ContentRequest? request)
    {
        request = null;
        if (!TryParse(url, method, out var uri, out var httpMethod))
            return false;
        if (httpMethod != HttpMethod.Get || !IsCacheable(uri)) {
            Interlocked.Increment(ref _skippedCount);
            return false;
        }

        request = new ContentRequest(uri) {
            Headers = range.IsNullOrEmpty()
                ? ImmutableDictionary<string, string>.Empty
                : ImmutableDictionary<string, string>.Empty.Add("Range", range),
        };
        return true;
    }

    private static bool TryParse(
        string? url, string? method,
        out Uri uri, out HttpMethod httpMethod)
    {
        uri = null!;
        httpMethod = HttpMethod.Get;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsedUri))
            return false;

        uri = parsedUri;
        Interlocked.Increment(ref _observedCount);
        if (method.IsNullOrEmpty())
            return true;

        try {
            httpMethod = HttpMethod.Parse(method);
            return true;
        }
        catch (Exception e) when (e is FormatException or ArgumentException) {
            return false;
        }
    }

    private static bool IsCacheable(Uri url)
        => url.Scheme == Uri.UriSchemeHttps && UrlMapper.IsOwnContentUrl(url.AbsoluteUri);

    private static FileSystemContentHandler? GetCache()
    {
        // Publication: built under the lock, read by every intercepting WebView thread
        if (Volatile.Read(ref _cache) is { } cache)
            return cache;

        lock (Lock) {
            if (_cache != null || _isCacheUnavailable)
                return _cache;

            var keys = MauiEncryptionKeys.Default;
            if (!keys.WhenReady.IsCompletedSuccessfully)
                return null; // Retried on the next request

            try {
                var settings = new FileSystemContentHandler.Options {
                    Directory = new FilePath(FileSystem.CacheDirectory) & "content",
                    EncryptionKey = keys.Primary,
                    MaxCachedLength = MaxCachedLength,
                    ResponseMaxAge = ResponseMaxAge,
                };
                Volatile.Write(ref _cache, new FileSystemContentHandler(
                    settings,
                    new HttpContentHandler(Client),
                    StaticLog.For<FileSystemContentHandler>()));
                DebugLog?.LogDebug("Content cache is at {Directory}", settings.Directory);
            }
            catch (Exception e) {
                _isCacheUnavailable = true;
                Log.LogWarning(e, "Content cache cannot be created");
            }
            return _cache;
        }
    }
}
