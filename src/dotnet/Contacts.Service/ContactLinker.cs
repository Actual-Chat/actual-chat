using ActualChat.Contacts.Db;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Contacts;

public class ContactLinker(IAccountsBackend accountsBackend, IContactsBackend contactsBackend, ICommander commander, IServiceProvider services) : ActivatedWorkerBase(services)
{
    private const int BatchSize = 100;

    private DbHub<ContactsDbContext> DbHub { get; } = services.DbHub<ContactsDbContext>();

    protected override async Task<bool> OnActivate(CancellationToken cancellationToken)
    {
        using var _1 = Tracer.Default.Region();
        var dbContext = DbHub.CreateDbContext(true);
        await using var _ = dbContext.ConfigureAwait(false);
        var dbExternalContactLinks = await dbContext.ExternalContactLinks.ForUpdate()
            .Where(x => !x.IsChecked)
            .OrderBy(x => x.Value)
            .Take(BatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (dbExternalContactLinks.Count == 0)
            return true;

        using var _2 = Tracer.Default.Region($"Checking {dbExternalContactLinks.Count} external contact link(s)");
        await dbExternalContactLinks.Select(EnsureCreated).Collect(HardwareInfo.ProcessorCount).ConfigureAwait(false);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return false;

        async Task EnsureCreated(DbExternalContactLink link)
        {
            try {
                var userId = await FindUserId(link, cancellationToken).ConfigureAwait(false);
                var ownerId = new ExternalContactId(link.DbExternalContactId).UserDeviceId.OwnerId;
                await EnsureContactExists(ownerId, userId, cancellationToken).ConfigureAwait(false);
                link.IsChecked = true;
            }
            catch (Exception e) {
                if (!e.IsCancellationOf(cancellationToken))
                    Log.LogError(e, "Failed to link external contact #{ExternalContactId} via {ExternalContactLink}",
                        link.DbExternalContactId, link.Value);
                throw;
            }
        }
    }

    private Task<UserId> FindUserId(DbExternalContactLink link, CancellationToken cancellationToken)
    {
        var phoneHash = link.ToPhoneHash();
        if (!phoneHash.IsNullOrEmpty())
            return accountsBackend.GetIdByPhoneHash(phoneHash, cancellationToken);

        var emailHash = link.ToEmailHash();
        if (!emailHash.IsNullOrEmpty())
            return accountsBackend.GetIdByEmailHash(emailHash, cancellationToken);

        Log.LogError("Unknown external contact link type: {ExternalContactLink}", link.Value);
        return Task.FromResult(UserId.None);
    }

    private async Task EnsureContactExists(UserId ownerId, UserId userId, CancellationToken cancellationToken)
    {
        if (userId.IsNone || ownerId == userId)
            return;

        var peerChatId = new PeerChatId(ownerId, userId);
        var contactId = new ContactId(ownerId, peerChatId);
        // check existing contact since command always performs db request
        var contact = await contactsBackend.Get(ownerId, contactId, cancellationToken).ConfigureAwait(false);
        if (contact.IsStored())
            return;

        contact = new Contact(contactId);
        // This command doesn't throw an exception in case contact already exists
        var createCmd = new ContactsBackend_Change(contactId, null, Change.Create(contact));
        await commander.Call(createCmd, cancellationToken).ConfigureAwait(false);
    }
}
