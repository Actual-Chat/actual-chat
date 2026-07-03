namespace ActualChat.Chat;

/// <summary>
/// Backend service implementation for distributed mesh cluster diagnostics.
/// </summary>
public class DiagnosticsBackend(IServiceProvider services) : IDiagnosticsBackend
{
    private DiagnosticsBackendLocal LocalBackend => field ??= services.GetRequiredService<DiagnosticsBackendLocal>();
    private MeshNode ThisNode => field ??= services.GetRequiredService<MeshNode>();

    public virtual Task<MeshDiagInfo> GetMeshInfo(
        NodeRef nodeRef,
        string tag,
        int extraLevel,
        CancellationToken cancellationToken)
        => LocalBackend.GetMeshInfo(tag, extraLevel, cancellationToken);

    public virtual Task<string> GetShardHostId(int shardKey, CancellationToken cancellationToken)
        => Task.FromResult(ThisNode.Ref.Value);

    public virtual Task<string> GetShardHostIdDirect(int shardKey, CancellationToken cancellationToken)
        => Task.FromResult(ThisNode.Ref.Value);
}
