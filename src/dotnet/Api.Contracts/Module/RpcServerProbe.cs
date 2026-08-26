using ActualChat.Rpc;
using ActualLab.Rpc.Clients;

namespace ActualChat.Module;

/// <summary>
/// Probes the server over plain HTTP to tell "the server is unreachable"
/// apart from "this RPC transport is broken", and to measure what a given
/// host can actually carry.
/// </summary>
public sealed class RpcServerProbe(IServiceProvider services)
{
    private const string ProbePath = "/rpc/check";
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    private HttpClient? _httpClient;

    // Assumed until a server answers without honoring "size", i.e. predates the sized probe.
    public bool IsSizedProbeSupported { get; private set; } = true;
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

    public async Task<TimeSpan?> MeasureTransfer(
        string host,
        int size,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var baseUrl = RpcEndpointSelector.WithHost(UrlMapper.BaseUrl.TrimSuffix("/"), host);
        var url = $"{baseUrl}{ProbePath}?size={size}";
        var httpClient = _httpClient ??= CreateHttpClient();
        var startedAt = CpuTimestamp.Now;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        try {
            var payload = await httpClient.GetByteArrayAsync(url, cts.Token).ConfigureAwait(false);
            var elapsed = CpuTimestamp.Now - startedAt;
            if (payload.Length >= size) {
                Log.LogInformation("RPC transfer probe to {Host}: {Size} bytes in {Elapsed}",
                    host, payload.Length, elapsed.ToShortString());
                return elapsed;
            }

            // A server predating the "size" parameter answers "ok", which proves reachability
            // but says nothing about throughput - the only thing this probe is for.
            IsSizedProbeSupported = false;
            Log.LogWarning("RPC transfer probe to {Host}: {Size} of {ExpectedSize} bytes",
                host, payload.Length, size);
            return null;
        }
        catch (Exception e) when (!cancellationToken.IsCancellationRequested) {
            Log.LogWarning(e, "RPC transfer probe to {Host}: FAILED after {Elapsed}",
                host, (CpuTimestamp.Now - startedAt).ToShortString());
            return null;
        }
    }

    // Private methods

    private HttpClient CreateHttpClient()
        => Services.GetRequiredService<RpcHttpClient>().Options.HttpClientFactory.Invoke(Services);
}
