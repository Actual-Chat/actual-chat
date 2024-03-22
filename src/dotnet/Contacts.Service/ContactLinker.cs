using ActualChat.Contacts.Db;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Contacts;

public class ContactLinker(IServiceProvider services) : ActivatedWorkerBase(services)
{
    private const int BatchSize = 100;

    private IAccountsBackend? _accountsBackend;
    private DbHub<ContactsDbContext>? _dbHub;
    private ICommander? _commander;

    private IAccountsBackend AccountsBackend => _accountsBackend ??= Services.GetRequiredService<IAccountsBackend>();
    private DbHub<ContactsDbContext> DbHub => _dbHub ??= Services.DbHub<ContactsDbContext>();
    private ICommander Commander => _commander ??= Services.Commander();

    protected override async Task<bool> OnActivate(CancellationToken cancellationToken)
    {
        using var _1 = Tracer.Default.Region();
        var dbContext = DbHub.CreateDbContext(true);
        await using var _ = dbContext.ConfigureAwait(false);
        var dbExternalContactLinks = await dbContext.ExternalContactLinks.ForUpdate()
            .Where(x => !x.IsChecked)
            .OrderBy(x => x.DbExternalContactId)
            .Take(BatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (dbExternalContactLinks.Count == 0)
            return true;

        using var _2 = Tracer.Default.Region($"Checking {dbExternalContactLinks.Count} external contact link(s)");
        foreach (var link in dbExternalContactLinks)
            try {
                var userId = await FindUserId(link, cancellationToken).ConfigureAwait(false);
                var ownerId = new ExternalContactId(link.DbExternalContactId).OwnerId;
                await EnsureContactExists(ownerId, userId, cancellationToken).ConfigureAwait(false);
                link.IsChecked = true;
            }
            catch (Exception e) {
                if (!e.IsCancellationOf(cancellationToken))
                    Log.LogError(e, "Failed to link external contact #{ExternalContactId} via {ExternalContactLink}",
                        link.DbExternalContactId, link.Value);
                throw;
            }
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return false;
    }

    private Task<UserId> FindUserId(DbExternalContactLink link, CancellationToken cancellationToken)
    {
        var phoneHash = link.ToPhoneHash();
        if (!phoneHash.IsNullOrEmpty())
            return AccountsBackend.GetIdByPhoneHash(phoneHash, cancellationToken);

        var emailHash = link.ToEmailHash();
        if (!emailHash.IsNullOrEmpty())
            return AccountsBackend.GetIdByEmailHash(emailHash, cancellationToken);

        Log.LogError("Unknown external contact link type: {ExternalContactLink}", link.Value);
        return Task.FromResult(UserId.None);
    }

    private async Task EnsureContactExists(UserId ownerId, UserId userId, CancellationToken cancellationToken)
    {
        if (userId.IsNone || ownerId == userId)
            return;

        var peerChatId = new PeerChatId(ownerId, userId);
        var contactId = new ContactId(ownerId, peerChatId);
        var contact = new Contact(contactId);

        // This command doesn't throw an exception in case contact already exists
        var createCmd = new ContactsBackend_Change(contactId, null, Change.Create(contact));
        await Commander.Call(createCmd, cancellationToken).ConfigureAwait(false);
    }
}
