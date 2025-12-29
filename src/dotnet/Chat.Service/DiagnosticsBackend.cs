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
