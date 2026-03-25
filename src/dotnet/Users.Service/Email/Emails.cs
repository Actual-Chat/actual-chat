using ActualChat.Users.Db;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Users.Email;

public class Emails(IServiceProvider services) : DbServiceBase<UsersDbContext>(services), IEmails
{
    private IEmailsBackend Backend { get; } = services.GetRequiredService<IEmailsBackend>();
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IChats Chats { get; } = services.GetRequiredService<IChats>();

    public virtual async Task<DigestPreview> GetDigestPreview(Session session, ChatId[] chatIds, DateTime? asOf, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustBeAdmin);
        if (chatIds.Length > 0) {
            foreach (var chatId in chatIds)
                await Chats.Get(session, chatId, cancellationToken).Require().ConfigureAwait(false);
        }
        return await Backend.GetDigestPreview(account.Id, chatIds, asOf, cancellationToken).ConfigureAwait(false);
    }

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
