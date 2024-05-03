using ActualChat.Contacts.Db;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Contacts;

public class ContactLinker(IServiceProvider services) : ActivatedWorkerBase(services)
{
    private const int BatchSize = 100;
    private Tracer? _tracer;

    private DbHub<ContactsDbContext> DbHub { get; } = services.DbHub<ContactsDbContext>();
    private IAccountsBackend AccountsBackend { get; } = services.GetRequiredService<IAccountsBackend>();
    private IContactsBackend ContactsBackend { get; } = services.GetRequiredService<IContactsBackend>();
    private ICommander Commander { get; } = services.Commander();
    private Tracer Tracer => _tracer ??= services.Tracer(GetType());

    protected override async Task<bool> OnActivate(CancellationToken cancellationToken)
    {
        using var _1 = Tracer.Region();
        var dbContext = await DbHub.CreateDbContext(readWrite: true, cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        var dbExternalContactLinks = await dbContext.ExternalContactLinks.ForUpdate()
            .Where(x => !x.IsChecked)
            .OrderBy(x => x.Value)
            .Take(BatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (dbExternalContactLinks.Count == 0)
            return true;

        using var _2 = Tracer.Region($"Checking {dbExternalContactLinks.Count} external contact link(s)");
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
        // check existing contact since command always performs db request
        var contact = await ContactsBackend.Get(ownerId, contactId, cancellationToken).ConfigureAwait(false);
        if (contact.IsStored())
            return;

        contact = new Contact(contactId);
        // This command doesn't throw an exception in case contact already exists
        var createCmd = new ContactsBackend_Change(contactId, null, Change.Create(contact));
        await Commander.Call(createCmd, cancellationToken).ConfigureAwait(false);
    }
}
