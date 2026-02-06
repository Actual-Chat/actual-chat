namespace ActualChat.Chat;

/// <summary>
/// Backend service implementation for distributed mesh cluster diagnostics.
/// </summary>
public class DiagnosticsBackend(IServiceProvider services) : IDiagnosticsBackend
{
    private DiagnosticsBackendLocal LocalBackend => field ??= services.GetRequiredService<DiagnosticsBackendLocal>();

    public virtual Task<MeshDiagInfo> GetMeshInfo(
        NodeRef nodeRef,
        string tag,
        int extraLevel,
        CancellationToken cancellationToken)
        => LocalBackend.GetMeshInfo(tag, extraLevel, cancellationToken);
}
