namespace ActualChat.Users;

public class AppUpdates(IServiceProvider services) : IAppUpdates
{
    private IAppUpdatesBackend Backend { get; } = services.GetRequiredService<IAppUpdatesBackend>();

    public virtual Task<AppUpdateInfo?> GetLatestUpdateInfo(AppKind appKind, CancellationToken cancellationToken)
        => Backend.GetLatestUpdateInfo(appKind, cancellationToken);
}
