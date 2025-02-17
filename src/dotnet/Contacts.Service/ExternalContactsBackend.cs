using ActualChat.Contacts.Db;
using ActualChat.Db;
using ActualChat.Hashing;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Contacts;

public class ExternalContactsBackend(IServiceProvider services) : DbServiceBase<ContactsDbContext>(services),
    IExternalContactsBackend
{
    private HostId HostId { get; } = services.GetRequiredService<HostId>();
    private ExternalContactHasher Hasher { get; } = services.GetRequiredService<ExternalContactHasher>();
    private IDbEntityResolver<string, DbExternalContact> DbExternalContactResolver { get; }
        = services.GetRequiredService<IDbEntityResolver<string, DbExternalContact>>();
    [field: AllowNull, MaybeNull]
    private IAccountsBackend AccountsBackend => field ??= Services.GetRequiredService<IAccountsBackend>();
    [field: AllowNull, MaybeNull]
    private ContactLinker ContactLinker => field ??= Services.GetRequiredService<ContactLinker>();

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

    // [ComputeMethod]
    public virtual async Task<string> GetDisplayNameFor(
        UserId ownerId,
        UserId peerUserId,
        CancellationToken cancellationToken)
    {
        var account = await AccountsBackend.Get(peerUserId, cancellationToken).ConfigureAwait(false);
        if (account is null)
            return "";

        var links = GetLinksFor(account.User);
        if (links.Length == 0)
            return "";

        var list = new List<ExternalContactId>();
        foreach (var link in links) {
            var extContactIds = await List(ownerId, link, cancellationToken).ConfigureAwait(false);
            list.AddRange(extContactIds);
        }
        if (list.Count == 0)
            return string.Empty;

        var extContacts = await list
            .Select(c => Get(c, cancellationToken))
            .Collect(cancellationToken)
            .ToApiArray()
            .ConfigureAwait(false);

        var extContact = extContacts
            .SkipNullItems()
            .OrderByDescending(c => c.ModifiedAt)
            .FirstOrDefault(c => !c.DisplayName.IsNullOrEmpty());

        return extContact?.DisplayName ?? "";
    }

    [ComputeMethod]
    protected virtual async Task<ImmutableArray<ExternalContactId>> List(UserId ownerId, string link, CancellationToken cancellationToken)
    {
        Log.LogInformation("-> List ('{OwnerId}', '{Link}')", ownerId, link);
        var idPrefix = ExternalContactId.Prefix(ownerId);
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var externalContactIds = await dbContext.ExternalContactLinks
            .Where(x => x.DbExternalContactId.StartsWith(idPrefix))
            .Where(x => x.Value == link)
            .Select(x => x.DbExternalContactId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return externalContactIds.Select(sid => new ExternalContactId(sid)).ToImmutableArray();
    }

    // Not compute method!
    public async Task<ApiSet<ExternalContactId>> ListReferencingContactIds(UserId userId, CancellationToken cancellationToken)
    {
        var account = await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
        if (account is null)
            return ApiSet<ExternalContactId>.Empty;

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var links = GetLinksFor(account.User);
        var externalContactIds = await dbContext.ExternalContactLinks
            .Where(x => links.Contains(x.Value))
            .Select(x => x.DbExternalContactId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return externalContactIds.Select(sid => new ExternalContactId(sid)).ToApiSet();
    }

    private static ImmutableArray<string> GetLinksFor(User user)
    {
        var list = ImmutableArray<string>.Empty;
        var phoneHash = user.GetPhoneHash();
        if (!phoneHash.IsNullOrEmpty())
            list = list.Add(DbExternalContactLink.GetPhoneLink(phoneHash));
        var emailHash = user.GetEmailHash();
        if (!emailHash.IsNullOrEmpty())
            list = list.Add(DbExternalContactLink.GetEmailLink(emailHash));
        return list;
    }

    // [CommandHandler]
    public virtual async Task<ApiArray<Result<ExternalContactFull?>>> OnBulkChange(
        ExternalContactsBackend_BulkChange command,
        CancellationToken cancellationToken)
    {
        const string hashesItemKey = "ModifiedItemHashesKey";
        var context = CommandContext.GetCurrent();

        if (Invalidation.IsActive) {
            var invUserDeviceIds = command.Changes.Select(x => x.Id.UserDeviceId).Distinct();
            foreach (var invId in invUserDeviceIds) {
 #pragma warning disable CS0618 // Type or member is obsolete
                _ = ListFull(invId.OwnerId, invId.DeviceId, default);
 #pragma warning restore CS0618 // Type or member is obsolete
                _ = List(invId, default);
            }
            var invIds = command.Changes.Select(x => x.Id);
            foreach (var invId in invIds)
                _ = Get(invId, default);

            var ownerId = command.Changes[0].Id.UserDeviceId.OwnerId;
            var invModifiedItemHashes = context.Operation.Items.Get<List<string>>(hashesItemKey)!;
            foreach (var hash in invModifiedItemHashes) {
                Log.LogInformation("-> OnBulkChange. Invalidate By hash ('{OwnerId}', '{Link}')", ownerId, hash);
                _ = List(ownerId, hash, default);
            }

            // NOTE(DF): force sync after changes are committed
            var isLocal = context.Operation.HostId == HostId.Id;
            if (isLocal && command.Changes.Any(x => x.Change.Kind is ChangeKind.Update or ChangeKind.Create))
                ContactLinker.Activate();
            return default!;
        }

        var modifiedItemHashes = new HashSet<string>();
        var result = new List<Result<ExternalContactFull?>>(command.Changes.Count);
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var someDeviceId = command.Changes.Select(c => c.Id.UserDeviceId).FirstOrDefault();
        await dbContext.ExternalContacts.Lock(someDeviceId, cancellationToken).ConfigureAwait(false);

        foreach (var itemChange in command.Changes)
            try {
                var externalContact = await ChangeItem(dbContext, itemChange, modifiedItemHashes, cancellationToken).ConfigureAwait(false);
                result.Add(new Result<ExternalContactFull?>(externalContact, null));
            }
            catch (Exception e) {
                Log.LogError(e,
                    "Failed to {ChangeKind} external contact #{ExternalContactId}",
                    itemChange.Change.Kind.ToString().ToLowerInvariant(),
                    itemChange.Id);
                result.Add(new Result<ExternalContactFull?>(null, e));
            }

        context.Operation.Items.Set(hashesItemKey, modifiedItemHashes.ToList());

        return result.ToApiArray();
    }

    private async Task<ExternalContactFull?> ChangeItem(
        ContactsDbContext dbContext,
        ExternalContactChange itemChange,
        ICollection<string> modifiedItemHashes,
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
        var externalContactFull = dbExternalContact.ToModel();
        if (change.IsUpdate(out _))
            AddHashes(existing!);
        AddHashes(externalContactFull);
        return externalContactFull;

        void AddHashes(ExternalContactFull externalContact1)
        {
            modifiedItemHashes.AddRange(externalContact1.PhoneHashes.Select(DbExternalContactLink.GetPhoneLink));
            modifiedItemHashes.AddRange(externalContact1.EmailHashes.Select(DbExternalContactLink.GetEmailLink));
        }
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
