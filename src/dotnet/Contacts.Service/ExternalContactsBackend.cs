using ActualChat.Contacts.Db;
using ActualChat.Hashing;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Contacts;

public class ExternalContactsBackend(IServiceProvider services) : DbServiceBase<ContactsDbContext>(services),
    IExternalContactsBackend
{
    private IAccountsBackend? _accountsBackend;
    private ContactLinker? _contactLinker;

    private HostId HostId { get; } = services.GetRequiredService<HostId>();
    private ExternalContactHasher Hasher { get; } = services.GetRequiredService<ExternalContactHasher>();
    private IDbEntityResolver<string, DbExternalContact> DbExternalContactResolver { get; }
        = services.GetRequiredService<IDbEntityResolver<string, DbExternalContact>>();
    private IAccountsBackend AccountsBackend => _accountsBackend ??= Services.GetRequiredService<IAccountsBackend>();
    private ContactLinker ContactLinker => _contactLinker ??= Services.GetRequiredService<ContactLinker>();

    // [ComputeMethod]
    [Obsolete("2024.04: Replaced with List - contact info list")]
    public virtual async Task<ApiArray<ExternalContactFull>> ListFull(UserId ownerId, Symbol deviceId, CancellationToken cancellationToken)
    {
        ownerId.Require();
        deviceId.Require();

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var idPrefix = ExternalContactId.Prefix(new UserDeviceId(ownerId, deviceId));
        var dbExternalContacts = await dbContext.ExternalContacts
            .Where(a => a.Id.StartsWith(idPrefix)) // This is faster than index-based approach
            .Include(x => x.ExternalContactLinks)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return dbExternalContacts.OrderBy(x => x.DisplayName, StringComparer.Ordinal)
            .Select(x => x.ToModel())
            .ToApiArray();
    }

    [ComputeMethod]
    public virtual async Task<ExternalContactFull?> Get(ExternalContactId externalContactId, CancellationToken cancellationToken)
    {
        var dbExternalContact = await DbExternalContactResolver.Get(externalContactId, cancellationToken).ConfigureAwait(false);
        return dbExternalContact?.ToModel();
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<ExternalContact>> List(
        UserDeviceId userDeviceId,
        CancellationToken cancellationToken)
    {
        userDeviceId.Require();

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var idPrefix = ExternalContactId.Prefix(userDeviceId);
        var dbExternalContacts = await dbContext.ExternalContacts
            .Where(a => a.Id.StartsWith(idPrefix)) // This is faster than index-based approach
            .Select(x => new { x.Id, x.Version, x.Hash })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return dbExternalContacts.Select(x =>
                new ExternalContact(new ExternalContactId(x.Id), x.Version) { Hash = new HashString(x.Hash) })
            .ToApiArray();
    }

    // Not compute method!
    public async Task<ApiSet<ExternalContactId>> ListReferencingContactIds(UserId userId, CancellationToken cancellationToken)
    {
        var account = await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
        if (account is null)
            return ApiSet<ExternalContactId>.Empty;

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var links = GetLinks().ToList();
        var externalContactIds = await dbContext.ExternalContactLinks
            .Where(x => links.Contains(x.Value))
            .Select(x => x.DbExternalContactId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return externalContactIds.Select(sid => new ExternalContactId(sid)).ToApiSet();

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
    public virtual async Task<ApiArray<Result<ExternalContactFull?>>> OnBulkChange(
        ExternalContactsBackend_BulkChange command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive) {
            var invIds = command.Changes.Select(x => x.Id.UserDeviceId).Distinct();
            foreach (var invId in invIds) {
 #pragma warning disable CS0618 // Type or member is obsolete
                _ = ListFull(invId.OwnerId, invId.DeviceId, default);
 #pragma warning restore CS0618 // Type or member is obsolete
                _ = List(invId, default);
            }
            // NOTE(DF): force sync after changes are committed
            var context = CommandContext.GetCurrent();
            var isLocal = context.Operation.HostId == HostId.Id;
            if (isLocal && command.Changes.Any(x => x.Change.Kind is ChangeKind.Update or ChangeKind.Create))
                ContactLinker.Activate();
            return default!;
        }

        var result = new List<Result<ExternalContactFull?>>(command.Changes.Count);
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var someDeviceId = command.Changes.Select(c => c.Id.UserDeviceId).FirstOrDefault();
        await dbContext.ExternalContacts.Lock(someDeviceId, cancellationToken).ConfigureAwait(false);

        foreach (var itemChange in command.Changes)
            try {
                var externalContact = await ChangeItem(itemChange, dbContext, cancellationToken).ConfigureAwait(false);
                result.Add(new Result<ExternalContactFull?>(externalContact, null));
            }
            catch (Exception e) {
                Log.LogError(e,
                    "Failed to {ChangeKind} external contact #{ExternalContactId}",
                    itemChange.Change.Kind.ToString().ToLowerInvariant(),
                    itemChange.Id);
                result.Add(new Result<ExternalContactFull?>(null, e));
            }
        return result.ToApiArray();
    }

    private async Task<ExternalContactFull?> ChangeItem(
        ExternalContactChange itemChange,
        ContactsDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var (id, expectedVersion, change) = itemChange;
        id.Require();
        change.RequireValid();

        // Can't use .ForUpdate() here due to join
        var dbExternalContact = await dbContext.ExternalContacts
            .Include(x => x.ExternalContactLinks)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            .ConfigureAwait(false);
        var existing = dbExternalContact?.ToModel();
        var now = Clocks.SystemClock.Now;

        if (change.IsCreate(out var externalContact)) {
            if (existing != null)
                return existing; // Already exists, so we don't recreate one

            externalContact = externalContact.WithHash(Hasher, false) with {
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
            externalContact = externalContact.WithHash(Hasher, false) with {
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
            dbExternalContact.RequireVersion(expectedVersion);
            dbContext.Remove(dbExternalContact);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false); // TODO(FC): bulk save
        return dbExternalContact.ToModel();
    }

    // [CommandHandler]
    public virtual async Task OnRemoveAccount(ExternalContactsBackend_RemoveAccount command, CancellationToken cancellationToken)
    {
        var userId = command.UserId;
        if (Invalidation.IsActive)
            return; // we can skip invalidation for own contacts

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
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
