using ActualChat.Users;

namespace ActualChat.Chat;

public class Diagnostics(IServiceProvider services) : IDiagnostics
{
    private DiagnosticsBackendLocal LocalBackend { get; } = services.GetRequiredService<DiagnosticsBackendLocal>();
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();

    public virtual async Task<MeshDiagInfo> GetMeshDiagInfo(Session session, string tag, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (!account.IsAdmin)
            throw StandardError.Unauthorized("Only admins can access.");

        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        var info = await LocalBackend.GetMeshDiagInfo(tag, 1, cancellationToken).ConfigureAwait(false);
        nodeIds.Add(info.ThisNodeId);
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
