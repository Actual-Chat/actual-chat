using ActualChat.Users;

namespace ActualChat.Chat;

public class Diagnostics : IDiagnostics
{
    public Diagnostics(IServiceProvider services)
    {
        LocalBackend = services.GetRequiredService<DiagnosticsBackendLocal>();
        Accounts = services.GetRequiredService<IAccounts>();
    }

    private DiagnosticsBackendLocal LocalBackend { get; }
    private IAccounts Accounts { get; }

    public virtual async Task<MeshDiagInfo> GetMeshDiagInfo(Session session, string tag, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (!account.IsAdmin)
            throw StandardError.Unauthorized("Only admins can access.");

        return await LocalBackend.GetMeshDiagInfo(tag, 1, cancellationToken).ConfigureAwait(false);
    }
}
