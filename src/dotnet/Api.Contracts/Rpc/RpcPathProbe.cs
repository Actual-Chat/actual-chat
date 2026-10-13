using System.Net.Sockets;
using System.Text;
using ActualLab.Rpc;

namespace ActualChat.Rpc;

/// <summary>
/// Checks a host over RPC itself: a peer of its own connects through the host, then a payload
/// larger than what some networks let through before capping a connection comes back over it.
/// </summary>
public sealed class RpcPathProbe(IServiceProvider services) : IRpcPathProbe
{
    public const int ProbeSize = 64 * 1024;
    // 64KB in the time left after connecting is about 87 kbps, far above a capped link
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan AttestationTimeout = TimeSpan.FromSeconds(3);

    private int _checkCount;

    private RpcHub RpcHub { get; } = services.RpcHub();
    private IRpcChecks Checks { get; } = services.GetRequiredService<IRpcChecks>();
    private ILogger Log { get; } = services.LogFor<RpcPathProbe>();

    public async Task<RpcPathMeasurement?> Check(string host, CancellationToken cancellationToken)
    {
        // A new ref per check: a finished peer stays in the hub for a while, and its ref would find it
        var relay = new RelayRef(host, Interlocked.Increment(ref _checkCount));
        var peer = RpcHub.GetClientPeer(relay.ToRpcRef());
        var startedAt = CpuTimestamp.Now;
        using var cts = cancellationToken.CreateLinkedTokenSource();
        cts.CancelAfter(Timeout);
        try {
            // The peer connects once and ends on the first error, so a check can't outlive its result
            await peer.WhenConnected(cts.Token).ConfigureAwait(false);
            var pingTime = await Measure(() => Checks.Ping(relay, cts.Token)).ConfigureAwait(false);
            var payloadTime = await Measure(() => Checks.GetPayload(relay, ProbeSize, cts.Token)).ConfigureAwait(false);
            // The server answers with less where it doesn't measure. Only a reply that never comes
            // indicts the host: a throttled link delivers the payload late or not at all.
            Log.LogInformation("RPC path check of {Host}: ping {PingTime}, payload {PayloadTime}",
                host, pingTime.ToShortString(), payloadTime.ToShortString());
            return new RpcPathMeasurement(pingTime, payloadTime);
        }
        catch (Exception e) when (!cancellationToken.IsCancellationRequested) {
            Log.LogWarning(e, "RPC path check of {Host}: failed after {Elapsed}",
                host, (CpuTimestamp.Now - startedAt).ToShortString());
            return null;
        }
        finally {
            peer.Dispose();
        }
    }

    public async Task<string?> GetAttestation(string host, CancellationToken cancellationToken)
    {
        using var cts = cancellationToken.CreateLinkedTokenSource();
        cts.CancelAfter(AttestationTimeout);
        try {
            using var client = new TcpClient();
            await client.ConnectAsync(host, RpcRelayAttestation.Port, cts.Token).ConfigureAwait(false);
            using var reader = new StreamReader(client.GetStream(), Encoding.ASCII);
            var attestation = await reader.ReadLineAsync(cts.Token).ConfigureAwait(false);
            if (RpcRelayAttestation.IsWellFormed(attestation))
                return attestation;

            Log.LogWarning("Relay {Host} answered with something that isn't an attestation", host);
            return null;
        }
        catch (Exception e) when (!cancellationToken.IsCancellationRequested) {
            Log.LogWarning(e, "Failed to get the attestation of {Host}", host);
            return null;
        }
    }

    // Private methods

    private static async Task<TimeSpan> Measure(Func<Task> call)
    {
        var startedAt = CpuTimestamp.Now;
        await call.Invoke().ConfigureAwait(false);
        return CpuTimestamp.Now - startedAt;
    }
}
