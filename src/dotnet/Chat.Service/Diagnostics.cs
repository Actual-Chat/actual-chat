using ActualChat.Flows;
using ActualChat.Flows.Infrastructure;
using ActualChat.Users;

namespace ActualChat.Chat;

/// <summary>
/// Frontend service for accessing mesh cluster diagnostics (admin-only).
/// </summary>
public class Diagnostics(IServiceProvider services) : IDiagnostics
{
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private DiagnosticsBackendLocal LocalBackend { get; } = services.GetRequiredService<DiagnosticsBackendLocal>();
    private MeshWatcher MeshWatcher { get; } = services.GetRequiredService<MeshWatcher>();
    private IDiagnosticsBackend Backend => field ??= services.GetRequiredService<IDiagnosticsBackend>();
    private IFlowBackend FlowBackend => field ??= services.GetRequiredService<IFlowBackend>();
    private FlowHub FlowHub => field ??= services.FlowHub();

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
        var nodeIds = new HashSet<string>() { info.ThisNodeId };
        return info with {
            Others = Flatten(info.Others, nodeIds).ToArray(),
        };
    }

    public virtual async Task<FlowTypeStat[]> GetFlowStats(Session session, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (!account.IsAdmin)
            throw StandardError.Unauthorized("Only admins can access.");

        return await FlowBackend.ListStats(default, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<FlowSummary[]> GetFlows(Session session, FlowsQuery query, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (!account.IsAdmin)
            throw StandardError.Unauthorized("Only admins can access.");

        return await FlowBackend.List(query, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<FlowDetails?> GetFlowDetails(Session session, string flowId, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (!account.IsAdmin)
            throw StandardError.Unauthorized("Only admins can access.");

        if (!FlowId.TryParse(flowId, out var id))
            return null;

        var flowData = await FlowBackend.TryGetData(id, cancellationToken).ConfigureAwait(false);
        if (flowData is null)
            return null;

        var flow = flowData.GetFlow(FlowHub);
        var error = flow.UntypedResult?.Error;
        var errorText = error is null ? null : $"{error.GetType().GetName()}: {error.Message}";
        return new FlowDetails(flowData.Console, errorText);
    }

    // Private methods

    private static IEnumerable<MeshDiagInfo> Flatten(MeshDiagInfo[] infos, HashSet<string> nodeIds)
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
