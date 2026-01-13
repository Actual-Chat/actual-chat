using ActualChat.Users;

namespace ActualChat.Chat;

public class Diagnostics(IServiceProvider services) : IDiagnostics
{
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private DiagnosticsBackendLocal LocalBackend { get; } = services.GetRequiredService<DiagnosticsBackendLocal>();
    private MeshWatcher MeshWatcher { get; } = services.GetRequiredService<MeshWatcher>();
    private IDiagnosticsBackend Backend => field ??= services.GetRequiredService<IDiagnosticsBackend>();

    public virtual async Task<MeshDiagInfo> GetMeshDiagInfo(Session session, string tag, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (!account.IsAdmin)
            throw StandardError.Unauthorized("Only admins can access.");

        // NOTE: Can't use diagnosticsBackend.GetMeshDiagInfo with theNode.Ref because it fails with NRE when launched in AspireHost.
        // ProxyTarget property of IDiagnosticsBackendProxy is NULL.
        //var diagnosticsBackend = Backend;
        //var info = await diagnosticsBackend.GetMeshDiagInfo(MeshWatcher.ThisNode.Ref, tag, 1, cancellationToken).ConfigureAwait(false);
        var info = await LocalBackend.GetMeshInfo(tag, 1, cancellationToken).ConfigureAwait(false);
        var nodeIds = new HashSet<string>(StringComparer.Ordinal) { info.ThisNodeId };
        return info with {
            Others = Flatten(info.Others, nodeIds).ToArray(),
        };
    }

    private IEnumerable<MeshDiagInfo> Flatten(MeshDiagInfo[] infos, HashSet<string> nodeIds)
    {
        foreach (var info in infos) {
            if (!nodeIds.Add(info.ThisNodeId))
                continue;

            yield return info;
            foreach (var child in Flatten(info.Others, nodeIds))
                yield return child;
        }
    }
}
