using ActualLab.Rpc.Clients;

namespace ActualChat.Module;

/// <summary>
/// Probes the server over plain HTTP to tell "the server is unreachable"
/// apart from "this RPC transport is broken".
/// </summary>
public sealed class RpcServerProbe(IServiceProvider services)
{
    private const string ProbePath = "/rpc/check";
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    private HttpClient? _httpClient;

    private IServiceProvider Services { get; } = services;
    private UrlMapper UrlMapper => field ??= Services.UrlMapper();
    private ILogger Log => field ??= Services.LogFor(GetType());

    public async Task<bool> IsServerReachable(CancellationToken cancellationToken)
    {
        var url = UrlMapper.BaseUrl.TrimSuffix("/") + ProbePath;
        var httpClient = _httpClient ??= CreateHttpClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(ProbeTimeout);
        try {
            using var response = await httpClient.GetAsync(url, cts.Token).ConfigureAwait(false);
            var isSuccess = response.IsSuccessStatusCode;
            Log.LogWarning("RPC probe to {Url}: {Status} ({StatusCode})",
                url, isSuccess ? "OK" : "FAILED", (int)response.StatusCode);
            return isSuccess;
        }
        catch (Exception e) {
            Log.LogWarning(e, "RPC probe to {Url}: FAILED", url);
            return false;
        }
    }

    // Private methods

    private HttpClient CreateHttpClient()
        => Services.GetRequiredService<RpcHttpClient>().Options.HttpClientFactory.Invoke(Services);
}
