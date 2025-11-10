using ActualChat.Users.Db;
using ActualChat.Users.Module;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Users.Email;

public class Emails(IServiceProvider services) : DbServiceBase<UsersDbContext>(services), IEmails
{
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();

    // [CommandHandler]
    public virtual async Task OnSendDigest(Emails_SendDigest command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var account = await Accounts.GetOwn(command.Session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustBeAdmin);
        var cmd = new EmailsBackend_SendDigest(account.Id) { IsDiagnosticsEnabled = true };
        await Commander.Call(cmd, cancellationToken).ConfigureAwait(false);
    }
}
