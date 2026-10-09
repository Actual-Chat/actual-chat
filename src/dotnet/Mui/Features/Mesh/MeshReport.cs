using System.Text;
using ActualLab.Rpc.Infrastructure;

namespace ActualChat.Mui;

public static class MeshReport
{
    private const string Separator = "***********************************************************";

    public static string Format(MeshDiagInfo info)
    {
        var sb = new StringBuilder();
        AppendNode(sb, info);
        foreach (var other in info.Others) {
            sb.AppendLine().AppendLine(Separator).AppendLine();
            AppendNode(sb, other);
        }
        return sb.ToString();
    }

    private static void AppendNode(StringBuilder sb, MeshDiagInfo info)
    {
        sb.AppendLine($"Mesh state ({info.Nodes.Length} node(s)):");
        for (var i = 0; i < info.Nodes.Length; i++) {
            var node = info.Nodes[i];
            var thisMark = node.IsThis ? ", this" : "";
            sb.AppendLine($"[{i}{thisMark}] = {node.Id} {node.Endpoint} : {node.State}");
            sb.AppendLine($"Roles: {node.Roles}");
        }

        sb.AppendLine().AppendLine("Mesh Rpc Peer Refs:");
        foreach (var rpcRef in info.MeshRpcRefs)
            sb.AppendLine($"{rpcRef.MeshRef} ==> {rpcRef.Route}");

        sb.AppendLine().AppendLine("Rpc Peers:");
        foreach (var peer in info.RpcPeers) {
            sb.AppendLine($"Id: {peer.Id}");
            sb.AppendLine(peer.Peer);
            sb.AppendLine($"ConnectionKind: {peer.ConnectionKind}");
            if (!IsLocal(peer)) {
                sb.AppendLine($"IsConnected: {peer.ConnectionStateKind}");
                sb.AppendLine($"Connection state: {peer.ConnectionInfo}");
            }
            sb.AppendLine();
        }
    }

    public static bool IsLocal(RpcPeerDiagInfo peer)
        => string.Equals(peer.ConnectionKind, "local", StringComparison.OrdinalIgnoreCase);

    public static bool IsProblem(RpcPeerDiagInfo peer)
        => !IsLocal(peer) && peer.ConnectionStateKind != RpcPeerConnectionStateKind.Connected;
}
