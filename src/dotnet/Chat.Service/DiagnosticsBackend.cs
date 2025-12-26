using System.Net.WebSockets;
using ActualChat.Rpc;
using ActualLab.Rpc;
using ActualLab.Rpc.Infrastructure;

namespace ActualChat.Chat;

public class DiagnosticsBackend(IServiceProvider services) : IDiagnosticsBackend
{
    private DiagnosticsBackendLocal LocalBackend => field ??= services.GetRequiredService<DiagnosticsBackendLocal>();

    public virtual Task<MeshDiagInfo> GetMeshDiagInfo(
        NodeRef nodeRef,
        string tag,
        int extraLevel,
        CancellationToken cancellationToken)
        => LocalBackend.GetMeshDiagInfo(tag, extraLevel, cancellationToken);
}

public class DiagnosticsBackendLocal : IComputeService, IHasDisposeStatus
{
    private readonly IServiceProvider _services;

    public DiagnosticsBackendLocal(IServiceProvider services)
    {
        _services = services;
        RpcHub = services.GetRequiredService<RpcHub>();
        MeshRpcPeerRefs = services.GetRequiredService<MeshRpcPeerRefs>();
    }

    private static readonly JsonSerializerOptions JsonSerializerOptions = new JsonSerializerOptions { WriteIndented = true };

    private IDiagnosticsBackend Backend => field ??= _services.GetRequiredService<IDiagnosticsBackend>();

    private RpcHub RpcHub { get; }
    private MeshRpcPeerRefs MeshRpcPeerRefs { get; }
    private MomentClockSet Clocks => field ??= _services.Clocks();
    private MeshWatcher MeshWatcher => MeshRpcPeerRefs.MeshWatcher;

    [ComputeMethod]
    public virtual async Task<MeshDiagInfo> GetMeshDiagInfo(string tag, int extraLevel, CancellationToken cancellationToken)
    {
        var thisInfo = await GetMeshDiagInfo(tag, cancellationToken).ConfigureAwait(false);
        if (extraLevel <= 0)
            return thisInfo;

        var meshState = MeshWatcher.State.Value;
        var otherNodes = meshState.AllNodes.Values.Where(c => !c.Equals(MeshWatcher.ThisNode)).ToArray();
        var infos = await otherNodes
            .Select(c => Backend.GetMeshDiagInfo(c.Ref, tag, extraLevel - 1, cancellationToken))
            .Collect(cancellationToken)
            .ConfigureAwait(false);
        return thisInfo with {
            Others = infos,
        };
    }

    [ComputeMethod]
    public virtual async Task<MeshDiagInfo> GetMeshDiagInfo(string tag, CancellationToken cancellationToken)
    {
        var meshState = await MeshWatcher.State.Use(cancellationToken).ConfigureAwait(false);
        var meshRpcPeerRefs = MeshRpcPeerRefs.RpcPeerRefs
            .Select(c => new MeshRpcPeerRefDiagInfo(
                c.Target.MeshRef.ToString(),
                c.ToString(),
                c.Address,
                c.Target.NodeRef.Value,
                c.Version,
                ""))
            .ToArray();
        var rpcPeers = RpcHub.InternalServices.Peers.Values
            .Select(c => new RpcPeerDiagInfo(
                c.Id.ToString(),
                c.ToString(),
                c.ConnectionKind.ToString(),
                c.IsConnected(),
                GetConnectionInfo(c.ConnectionState.Value),
                ""))
            .ToArray();
        var now = Clocks.SystemClock.Now;
        var nodes = meshState.AllNodes
            .Values.Order()
            .Select(c => new NodeDiagInfo(
                c.Ref.Value,
                c.Endpoint,
                c.State.ToString(),
                MeshWatcher.ThisNode.Equals(c),
                c.DeadAt.HasValue ? (c.DeadAt.Value - now).Positive() : null,
                c.Roles.ToDelimitedString(", "),
                ""))
            .ToArray();

        return new MeshDiagInfo(
            MeshWatcher.ThisNode.Ref.Value,
            tag,
            now,
            nodes,
            rpcPeers,
            meshRpcPeerRefs,
            [],
            "");
    }

    private string GetConnectionInfo(RpcPeerConnectionState state) {
        var rpcConnection = state.Connection;
        var info = new ConnectionStateDiagInfo(
            state.Handshake,
            state.Error?.ToString() ?? "",
            state.TryIndex,
            state.ReaderTokenSource?.IsCancellationRequested ?? false
        );
        if (rpcConnection is not null) {
            var websocket = rpcConnection.Properties.KeylessGet<WebSocket>();
            var uri = rpcConnection.Properties.KeylessGet<Uri>();
            var connectionInfo = new RpcConnectionDiagInfo(rpcConnection.IsLocal, uri?.ToString() ?? "", websocket is not null ? GetWebsocketInfo(websocket) : null);
            info = info with {
                Connection = connectionInfo,
            };
        }
        return JsonSerializer.Serialize(info, JsonSerializerOptions);
    }

    private WebSocketDiagInfo GetWebsocketInfo(WebSocket websocket)
        => new(
            websocket.ToString() ?? "",
            websocket.State.ToString(),
            websocket.SubProtocol,
            websocket.CloseStatus?.ToString() ?? "",
            websocket.CloseStatusDescription);

    // Nested types
    public record ConnectionStateDiagInfo(
        RpcHandshake? Handshake,
        string? Error,
        int TryIndex,
        bool IsCancelled) {
        public RpcConnectionDiagInfo? Connection { get; init; }
    }

    public record RpcConnectionDiagInfo(bool IsLocal, string Uri, WebSocketDiagInfo? WebSocket);

    public record WebSocketDiagInfo(string Websocket, string State, string? SubProtocol, string CloseStatus, string? CloseStatusDescription);

    public bool IsDisposed => false;
}
