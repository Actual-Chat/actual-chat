using System.Net;
using ActualChat.Users.Module;

namespace ActualChat.Users.AppStores;

/// <summary>
/// Shared HTTP plumbing for the store probes: one named client, a browser-like
/// User-Agent, and a response size cap.
/// </summary>
public abstract class StoreProbe(IServiceProvider services) : IStoreProbe
{
    public const string HttpClientName = nameof(StoreProbe);
    // Google Play answers anything else with a consent stub that carries no version
    private const string UserAgentValue =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) "
        + "Chrome/128.0.0.0 Safari/537.36";
    private const int MaxResponseSize = 8 * 1024 * 1024;
    private IHttpClientFactory HttpClientFactory => field ??= Services.HttpClientFactory();
    protected IServiceProvider Services { get; } = services;
    protected AppUpdateSettings Settings { get; } = services.GetRequiredService<UsersSettings>().AppUpdates;
    protected ILogger Log => field ??= Services.LogFor(GetType());

    public abstract Task<StoreProbeResult?> Probe(string storeId, CancellationToken cancellationToken);

    // Protected/internal methods

    protected async Task<string?> Fetch(Uri uri, CancellationToken cancellationToken)
    {
        // Returns null when the store says the app isn't there; every other failure throws
        using var client = HttpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgentValue);
        request.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
        using var response = await client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            return null;

        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxResponseSize)
            throw StandardError.Constraint($"{uri.Host} returned more than {MaxResponseSize} bytes.");

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var _ = stream.ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        var buffer = new char[16 * 1024];
        var sb = ActualLab.Text.StringBuilderExt.Acquire();
        while (true) {
            var readCount = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (readCount == 0)
                break;

            sb.Append(buffer, 0, readCount);
            if (sb.Length > MaxResponseSize)
                throw StandardError.Constraint($"{uri.Host} returned more than {MaxResponseSize} bytes.");
        }

        return sb.ToStringAndRelease();
    }
}
