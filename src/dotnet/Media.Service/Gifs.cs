using ActualChat.Media.Module;
using ActualChat.Resilience;
using ActualLab.Rpc.Infrastructure;

namespace ActualChat.Media;

public class Gifs(IServiceProvider services) : IGifs
{
    public const string HttpClientName = nameof(Gifs);
    private const int PerPage = 24;
    private const string BaseUrl = "https://api.klipy.com/api/v1";

    private IHttpClientFactory HttpClientFactory { get; } = services.GetRequiredService<IHttpClientFactory>();
    private HttpClient HttpClient => field ??= HttpClientFactory.CreateClient(HttpClientName);
    private string ApiKey { get; } = services.GetRequiredService<MediaSettings>().KlipyApiKey;
    private RateLimitPolicy RateLimitPolicy { get; }
        = services.GetService<RateLimitPolicy>() ?? RateLimitPolicy.Unlimited;
    private RateLimitIdentityResolver IdentityResolver { get; }
        = services.GetService<RateLimitIdentityResolver>() ?? new RateLimitIdentityResolver(services);
    private ILogger<Gifs> Log { get; } = services.LogFor<Gifs>();

    public async Task<GifSearchResult> Search(string query, int page, CancellationToken cancellationToken)
    {
        if (ApiKey.IsNullOrEmpty())
            return new GifSearchResult([], false);

        var url = $"{BaseUrl}/{ApiKey}/gifs/search?q={Uri.EscapeDataString(query)}&per_page={PerPage}&page={page}&rating=pg";
        return await Fetch(nameof(Search), url, cancellationToken).ConfigureAwait(false);
    }

    public async Task<GifSearchResult> GetTrending(int page, CancellationToken cancellationToken)
    {
        if (ApiKey.IsNullOrEmpty())
            return new GifSearchResult([], false);

        var url = $"{BaseUrl}/{ApiKey}/gifs/trending?per_page={PerPage}&page={page}";
        return await Fetch(nameof(GetTrending), url, cancellationToken).ConfigureAwait(false);
    }

    private async Task<GifSearchResult> Fetch(string method, string url, CancellationToken cancellationToken)
    {
        await CheckRateLimits($"{nameof(Gifs)}.{method}", cancellationToken).ConfigureAwait(false);

        try {
            using var response = await HttpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<KlipyResponse>(json);
            if (result?.Data?.Items is not { } items)
                return new GifSearchResult([], false);

            var gifItems = items
                .Select(ToGifItem)
                .Where(x => x is not null)
                .ToArray();
            return new GifSearchResult(gifItems!, result.Data.HasNext);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Failed to fetch GIFs from Klipy");
            return new GifSearchResult([], false);
        }
    }

    private async Task CheckRateLimits(string method, CancellationToken cancellationToken)
    {
        var source = RateLimitSource.ForConnection(RpcInboundContext.Current?.Peer.ConnectionState.Value.Connection);
        var identities = new RateLimitIdentity[RateLimitIdentityResolver.MaxIdentityCount];
        var identityCount = await IdentityResolver
            .Resolve(RateLimitPolicy, RateLimitClass.GifProvider, source, identities, cancellationToken)
            .ConfigureAwait(false);
        await RateLimitPolicy
            .Check(method, RateLimitClass.GifProvider, identities.AsSpan(0, identityCount), cancellationToken)
            .ConfigureAwait(false);
    }

    private static GifItem? ToGifItem(KlipyItem item)
    {
        var preview = item.File?.Sm?.Gif ?? item.File?.Sm?.Webp;
        var full = item.File?.Md?.Gif ?? item.File?.Hd?.Gif;
        if (preview is null || full is null)
            return null;

        return new GifItem(
            item.Slug ?? "",
            item.Title ?? "",
            preview.Url,
            preview.Width,
            preview.Height,
            full.Url,
            full.Width,
            full.Height,
            item.BlurPreview ?? "");
    }

    // Klipy JSON models

    private sealed class KlipyResponse
    {
        [JsonPropertyName("result")] public bool Result { get; set; }
        [JsonPropertyName("data")] public KlipyData? Data { get; set; }
    }

    private sealed class KlipyData
    {
        [JsonPropertyName("data")] public KlipyItem[]? Items { get; set; }
        [JsonPropertyName("has_next")] public bool HasNext { get; set; }
    }

    private sealed class KlipyItem
    {
        [JsonPropertyName("slug")] public string? Slug { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("file")] public KlipyFile? File { get; set; }
        [JsonPropertyName("blur_preview")] public string? BlurPreview { get; set; }
    }

    private sealed class KlipyFile
    {
        [JsonPropertyName("hd")] public KlipyFormats? Hd { get; set; }
        [JsonPropertyName("md")] public KlipyFormats? Md { get; set; }
        [JsonPropertyName("sm")] public KlipyFormats? Sm { get; set; }
        [JsonPropertyName("xs")] public KlipyFormats? Xs { get; set; }
    }

    private sealed class KlipyFormats
    {
        [JsonPropertyName("gif")] public KlipyMedia? Gif { get; set; }
        [JsonPropertyName("webp")] public KlipyMedia? Webp { get; set; }
    }

    private sealed class KlipyMedia
    {
        [JsonPropertyName("url")] public string Url { get; set; } = "";
        [JsonPropertyName("width")] public int Width { get; set; }
        [JsonPropertyName("height")] public int Height { get; set; }
    }
}
