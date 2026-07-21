using System.Net.WebSockets;
using ActualChat.Rpc;
using ActualLab.Rpc;
using ActualLab.Rpc.Infrastructure;

namespace ActualChat.Chat;

public class DiagnosticsBackendLocal(IServiceProvider services) : IComputeService, IHasDisposeStatus
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new() { WriteIndented = true };

    private IDiagnosticsBackend Backend => field ??= services.GetRequiredService<IDiagnosticsBackend>();
    private RpcHub RpcHub { get; } = services.GetRequiredService<RpcHub>();
    private MeshRpcRefs MeshRpcRefs { get; } = services.GetRequiredService<MeshRpcRefs>();
    private MomentClockSet Clocks => field ??= services.Clocks();
    private MeshWatcher MeshWatcher => MeshRpcRefs.MeshWatcher;
    private ILogger Log => field ??= services.LogFor(GetType());
    public bool IsDisposed => false;

    [ComputeMethod]
    public virtual async Task<MeshDiagInfo> GetMeshInfo(string tag, int extraLevel, CancellationToken cancellationToken)
    {
        var thisInfo = await GetMeshInfo(tag, cancellationToken).ConfigureAwait(false);
        if (extraLevel <= 0)
            return thisInfo;

        var thisNode = MeshWatcher.ThisNode;
        var meshState = MeshWatcher.State.Value;
        var otherNodes = meshState.AllNodes.Values.Where(c => c != thisNode).ToArray();
        var infos = await otherNodes
            .Where(c => c.State is MeshNodeState.Online)
            .Select(async c => {
                var diagnosticsBackend = Backend;
                try {
                    var otherInfo = await diagnosticsBackend.GetMeshInfo(c.Ref, tag, extraLevel - 1, cancellationToken)
                        .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
                    return otherInfo;
                }
                catch (Exception ex) {
                    Log.LogError(ex, "Failed to get mesh diag info for {Node}", c.Ref);
                    return null;
                }
            })
            .Collect(cancellationToken)
            .ConfigureAwait(false);
        return thisInfo with {
            Others = infos.SkipNullItems().ToArray(),
        };
    }

    [ComputeMethod]
    public virtual async Task<MeshDiagInfo> GetMeshInfo(string tag, CancellationToken cancellationToken)
    {
        var meshState = await MeshWatcher.State.Use(cancellationToken).ConfigureAwait(false);
        var meshRpcRefs = MeshRpcRefs.RpcRefs
            .OrderBy(c => c.ShardRef.Scheme.Name)
            .ThenBy(c => c.ShardRef.Key)
            .Select(c => new MeshRpcRefDiagInfo(
                c.MeshRef.ToString(),
                c.Route.ToString(),
                c.Address,
                c.Resolved.NodeRef.Value,
                c.Route.Version,
                ""))
            .ToArray();
        var rpcPeers = RpcHub.InternalServices.Peers.Values
            .Select(c => new { Ref = c.Ref as MeshRpcRef, Peer = c })
            .Select(c => new {
                SchemeName = c.Ref?.ShardRef.Scheme.Name ?? "",
                ShardKey = c.Ref?.ShardRef.Key ?? 0,
                c.Peer
            })
            .OrderBy(c => c.SchemeName)
            .ThenBy(c => c.ShardKey)
            .ThenBy(c => c.Peer.Ref.HostInfo)
            .Select(c => c.Peer)
            .Select(c => new RpcPeerDiagInfo(
                c.Id.ToString(),
                c.ToString(),
                c.ConnectionKind.ToString(),
                c.ConnectionState.Value.Kind,
                GetConnectionInfo(c.ConnectionState.Value),
                ""))
            .ToArray();

        var now = Clocks.SystemClock.Now;
        var thisNode = MeshWatcher.ThisNode;
        var nodes = meshState.AllNodes
            .Values.Order()
            .Select(c => new NodeDiagInfo(
                c.Ref.Value,
                c.Endpoint,
                c.State.ToString(),
                thisNode == c,
                c.Roles.ToDelimitedString(", "),
                ""))
            .ToArray();

        return new MeshDiagInfo(
            thisNode.Ref.Value,
            tag,
            now,
            nodes,
            rpcPeers,
            meshRpcRefs,
            [],
            "");
    }

    // Private methods

    private static string GetConnectionInfo(RpcPeerConnectionState state)
    {
        var rpcConnection = state.Connection;
        var info = new ConnectionStateDiagInfo(
            state.Handshake,
            state.Error?.ToString() ?? "",
            state.ConnectionAttemptIndex,
            state.ReaderTokenSource?.IsCancellationRequested ?? false
        );
        if (rpcConnection is not null) {
            var websocket = rpcConnection.Properties.KeylessGet<WebSocket>();
            var uri = rpcConnection.Properties.KeylessGet<Uri>();
            var connectionInfo = new RpcConnectionDiagInfo(
                rpcConnection.IsLocal,
                uri?.ToString() ?? "",
                websocket is not null ? GetWebSocketInfo(websocket) : null);
            info = info with {
                Connection = connectionInfo,
            };
        }
        return JsonSerializer.Serialize(info, JsonSerializerOptions);
    }

    private static WebSocketDiagInfo GetWebSocketInfo(WebSocket websocket)
        => new(
            websocket.ToString() ?? "",
            websocket.State.ToString(),
            websocket.SubProtocol,
            websocket.CloseStatus?.ToString() ?? "",
            websocket.CloseStatusDescription);

    // Nested types

    public sealed record ConnectionStateDiagInfo(
        RpcHandshake? Handshake,
        string? Error,
        int TryIndex,
        bool IsCancelled)
    {
        public RpcConnectionDiagInfo? Connection { get; init; }
    }

    public sealed record RpcConnectionDiagInfo(
        bool IsLocal,
        string Uri,
        WebSocketDiagInfo? WebSocket);

    public sealed record WebSocketDiagInfo(
        string Websocket,
        string State,
        string? SubProtocol,
        string CloseStatus,
        string? CloseStatusDescription);
}
