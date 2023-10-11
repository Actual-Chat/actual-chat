using ActualChat.Contacts.Db;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Contacts;

public class ExternalContactsBackend(IServiceProvider services) : DbServiceBase<ContactsDbContext>(services),
    IExternalContactsBackend
{
    private IAccountsBackend AccountsBackend { get; } = services.GetRequiredService<IAccountsBackend>();

    // [ComputeMethod]
    public virtual async Task<ApiArray<ExternalContact>> List(UserId ownerId, Symbol deviceId, CancellationToken cancellationToken)
    {
        ownerId.Require();

        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var idPrefix = ExternalContactId.Prefix(ownerId, deviceId);
        var dbExternalContacts = await dbContext.ExternalContacts
            .Include(x => x.ExternalContactLinks)
            .Where(a => a.Id.StartsWith(idPrefix)) // This is faster than index-based approach
            .OrderBy(a => a.DisplayName)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return dbExternalContacts.Select(x => x.ToModel()).ToApiArray();
    }

    // Not compute method!
    public async Task<ApiSet<UserId>> ListReferencingUserIds(UserId userId, CancellationToken cancellationToken)
    {
        var account = await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
        if (account is null)
            return ApiSet<UserId>.Empty;

        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var links = GetLinks().ToList();
        var externalContactIds = await dbContext.ExternalContactLinks
            .Where(x => links.Contains(x.Value))
            .Select(x => x.DbExternalContactId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return externalContactIds.Select(sid => new ExternalContactId(sid).OwnerId).ToApiSet();

        IEnumerable<string> GetLinks()
        {
            var phoneHash = account.User.GetPhoneHash();
            if (!phoneHash.IsNullOrEmpty())
                yield return DbExternalContactLink.GetPhoneLink(phoneHash);

            var emailHash = account.User.GetEmailHash();
            if (!emailHash.IsNullOrEmpty())
                yield return DbExternalContactLink.GetEmailLink(emailHash);
        }
    }

    // [CommandHandler]
    public virtual async Task<ExternalContact?> OnChange(
        ExternalContactsBackend_Change command,
        CancellationToken cancellationToken)
    {
        var (id, expectedVersion, change) = command;
        var ownerId = id.OwnerId;
        var deviceId = id.DeviceId;

        if (Computed.IsInvalidating()) {
            _ = List(ownerId, deviceId, default);
            return default!;
        }

        id.Require();
        ownerId.Require();
        change.RequireValid();

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbExternalContact = await dbContext.ExternalContacts.ForUpdate()
            .Include(x => x.ExternalContactLinks)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            .ConfigureAwait(false);
        var existing = dbExternalContact?.ToModel();
        var now = Clocks.SystemClock.Now;

        if (change.IsCreate(out var externalContact)) {
            if (existing != null)
                return existing; // Already exists, so we don't recreate one

            externalContact = externalContact with {
                Id = id,
                Version = VersionGenerator.NextVersion(),
                CreatedAt = now,
                ModifiedAt = now,
            };
            dbExternalContact = new DbExternalContact(externalContact);
            dbContext.Add(dbExternalContact);
        }
        else if (change.IsUpdate(out externalContact)) {
            dbExternalContact.RequireVersion(expectedVersion);
            externalContact = externalContact with {
                Version = VersionGenerator.NextVersion(dbExternalContact.Version),
                ModifiedAt = now,
            };
            dbExternalContact.UpdateFrom(externalContact);
        }
        else { // Remove
            if (expectedVersion != null)
                dbExternalContact.RequireVersion(expectedVersion);
            if (dbExternalContact == null)
                return null;

            dbContext.Remove(dbExternalContact);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        externalContact = dbExternalContact.ToModel();
        if (change.Kind is ChangeKind.Update or ChangeKind.Create)
            await CreateMissingContacts(ownerId, externalContact, existing, cancellationToken).ConfigureAwait(false);
        return externalContact;
    }

    // [CommandHandler]
    public virtual async Task OnRemoveAccount(ExternalContactsBackend_RemoveAccount command, CancellationToken cancellationToken)
    {
        var userId = command.UserId;
        if (Computed.IsInvalidating())
            return; // we can skip invalidation for own contacts

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var idPrefix = ExternalContactId.Prefix(userId);
        // we remove contacts without invalidation since nobody else sees these contacts
        await dbContext.ExternalContacts
            .Where(a => a.Id.StartsWith(idPrefix)) // This is faster than index-based approach
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task CreateMissingContacts(
        UserId ownerId,
        ExternalContact externalContact,
        ExternalContact? existing,
        CancellationToken cancellationToken)
    {
        var addedPhoneHashes = existing != null
            ? externalContact.PhoneHashes.Where(x => !existing.PhoneHashes.Contains(x))
            : externalContact.PhoneHashes;
        foreach (var phoneHash in addedPhoneHashes) {
            var userId = await AccountsBackend.GetIdByPhoneHash(phoneHash, cancellationToken).ConfigureAwait(false);
            await CreateContact(ownerId, userId, cancellationToken).ConfigureAwait(false);
        }

        var addedEmailHashes = existing != null
            ? externalContact.EmailHashes.Where(x => !existing.EmailHashes.Contains(x))
            : externalContact.EmailHashes;
        foreach (var emailHash in addedEmailHashes) {
            var userId = await AccountsBackend.GetIdByEmailHash(emailHash, cancellationToken).ConfigureAwait(false);
            await CreateContact(ownerId, userId, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task CreateContact(UserId ownerId, UserId userId, CancellationToken cancellationToken)
    {
        if (userId.IsNone || ownerId == userId)
            return;

        var peerChatId = new PeerChatId(ownerId, userId);
        var contactId = new ContactId(ownerId, peerChatId);

        try {
            var contact = new Contact(contactId);
            var cmd = new ContactsBackend_Change(contactId, null, Change.Create(contact));
            await Commander.Call(cmd, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to create contact #{ContactId} from external contact", contactId);
        }
    }
}
