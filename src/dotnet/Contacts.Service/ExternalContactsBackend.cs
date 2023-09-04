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
            .Include(x => x.ExternalPhones)
            .Include(x => x.ExternalEmails)
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

        IEnumerable<UserId> all = Enumerable.Empty<UserId>();
        var phone = account.User.GetPhone();
        if (!phone.IsNone) {
            var byPhone = await ListIds(dbContext.ExternalPhones
                    .Where(x => x.Phone == (string)phone)
                    .Select(x => x.DbExternalContactId))
                .ConfigureAwait(false);
            all = all.Concat(byPhone);
        }
        var email = account.User.GetEmail();
        if (!email.IsNullOrEmpty()) {
            var byEmail = await ListIds(dbContext.ExternalEmails
                    .Where(x => x.Email == email)
                    .Select(x => x.DbExternalContactId))
                .ConfigureAwait(false);
            all = all.Concat(byEmail);
        }
        return all.ToApiSet();

        async Task<IEnumerable<UserId>> ListIds(IQueryable<string> idsQuery)
        {
            var list = await idsQuery.ToListAsync(cancellationToken).ConfigureAwait(false);
            return list.Select(sid => new ExternalContactId(sid).OwnerId);
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
            .Include(x => x.ExternalPhones)
            .Include(x => x.ExternalEmails)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            .ConfigureAwait(false);
        var existing = dbExternalContact?.ToModel();

        if (change.IsCreate(out var externalContact)) {
            if (existing != null)
                return existing; // Already exists, so we don't recreate one

            var now = Clocks.SystemClock.Now;
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
            return; // spawns commands to remove contacts for other owners, we can skip invalidation for own contacts

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
        var addedPhones = existing != null
            ? externalContact.Phones.Where(x => !existing.Phones.Contains(x))
            : externalContact.Phones;
        foreach (var phone in addedPhones) {
            var userId = await AccountsBackend.GetIdByPhone(phone, cancellationToken).ConfigureAwait(false);
            await CreateContact(ownerId, userId, cancellationToken).ConfigureAwait(false);
        }

        var addedEmails = existing != null
            ? externalContact.Emails.Where(x => !existing.Emails.Contains(x))
            : externalContact.Emails;
        foreach (var email in addedEmails) {
            var userId = await AccountsBackend.GetIdByEmail(email, cancellationToken).ConfigureAwait(false);
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
