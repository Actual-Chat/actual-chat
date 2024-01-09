using ActualChat.Contacts.Db;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Contacts;

public class ExternalContactsBackend(IServiceProvider services) : DbServiceBase<ContactsDbContext>(services),
    IExternalContactsBackend
{
    private IAccountsBackend? _accountsBackend;
    private ContactLinkingJob? _contactLinkingJob;

    private IAccountsBackend AccountsBackend => _accountsBackend ??= Services.GetRequiredService<IAccountsBackend>();
    private ContactLinkingJob ContactLinkingJob => _contactLinkingJob ??= Services.GetRequiredService<ContactLinkingJob>();

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
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return dbExternalContacts.OrderBy(x => x.DisplayName, StringComparer.Ordinal)
            .Select(x => x.ToModel())
            .ToApiArray();
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
    public virtual async Task<ApiArray<Result<ExternalContact?>>> OnBulkChange(
        ExternalContactsBackend_BulkChange command,
        CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating()) {
            var invIds = command.Changes.Select(x => x.Id).DistinctBy(x => (x.OwnerId, x.DeviceId));
            foreach (var invId in invIds)
                _ = List(invId.OwnerId, invId.DeviceId, default);
            return default!;
        }

        var result = new List<Result<ExternalContact?>>(command.Changes.Count);
        foreach (var itemChange in command.Changes)
            try {
                var externalContact = await ChangeItem(itemChange, cancellationToken).ConfigureAwait(false);
                result.Add(new Result<ExternalContact?>(externalContact, null));
            }
            catch (Exception e) {
                Log.LogError(e,
                    "Failed to {ChangeKind} external contact #{ExternalContactId}",
                    itemChange.Change.Kind.ToString().ToLowerInvariant(),
                    itemChange.Id);
                result.Add(new Result<ExternalContact?>(null, e));
            }
        if (command.Changes.Any(x => x.Change.Kind is ChangeKind.Update or ChangeKind.Create))
            ContactLinkingJob.OnSyncNeeded();
        return result.ToApiArray();
    }

    private async Task<ExternalContact?> ChangeItem(
        ExternalContactChange itemChange,
        CancellationToken cancellationToken)
    {
        var (id, expectedVersion, change) = itemChange;
        var ownerId = id.OwnerId;

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
            dbContext.ExternalContacts.Update(dbExternalContact);
        }
        else {
            // Remove
            if (dbExternalContact == null)
                return null;
            if (expectedVersion != null)
                dbExternalContact.RequireVersion(expectedVersion);

            dbContext.Remove(dbExternalContact);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false); // TODO(FC): bulk save
        externalContact = dbExternalContact.ToModel();
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
}
