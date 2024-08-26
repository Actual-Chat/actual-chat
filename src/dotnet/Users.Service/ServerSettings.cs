using ActualChat.Kvas;

namespace ActualChat.Users;

public class ServerSettings : IServerSettings
{
    private IAccounts Accounts { get; }
    private IKvas Kvas { get; }

    public ServerSettings(IServiceProvider services)
    {
        Accounts = services.GetRequiredService<IAccounts>();
        var serverKvasBackend = services.GetRequiredService<IServerKvasBackend>();
        Kvas = serverKvasBackend.GetServerSettingsClient();
    }

    public virtual async Task<byte[]?> Get(Session session, string key, CancellationToken cancellationToken = default)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustBeAdmin);
        return await Kvas.Get(key, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task OnSet(ServerSettings_Set command, CancellationToken cancellationToken = default)
    {
        var (session, key, value) = command;
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustBeAdmin);
        await Kvas.Set(key, value, cancellationToken).ConfigureAwait(false);
    }
}
