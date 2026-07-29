using ActualChat.Contacts.Db;
using ActualChat.Db;
using ActualChat.Hashing;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Contacts;

/// <summary>
/// Backend service implementation for managing external contacts (phone contacts).
/// </summary>
public class ExternalContactsBackend(IServiceProvider services) : DbServiceBase<ContactsDbContext>(services),
    IExternalContactsBackend
{
    // Far above any real address book - it's here so the result can't be unbounded
    private const int MaxListCount = 25_000;
    private HostId HostId { get; } = services.GetRequiredService<HostId>();
    private ExternalContactHasher Hasher { get; } = services.GetRequiredService<ExternalContactHasher>();
    private IDbEntityResolver<string, DbExternalContact> DbExternalContactResolver { get; }
        = services.GetRequiredService<IDbEntityResolver<string, DbExternalContact>>();
    private IAccountsBackend AccountsBackend => field ??= Services.GetRequiredService<IAccountsBackend>();
    private ContactLinker ContactLinker => field ??= Services.GetRequiredService<ContactLinker>();

    [ComputeMethod]
    public virtual async Task<ExternalContactFull?> Get(ExternalContactId externalContactId, CancellationToken cancellationToken)
    {
        var dbExternalContact = await DbExternalContactResolver.Get(externalContactId.Value, cancellationToken).ConfigureAwait(false);
        return dbExternalContact?.ToModel();
    }

    // [ComputeMethod]
    public virtual async Task<ExternalContact[]> List(
        UserDeviceId userDeviceId,
        CancellationToken cancellationToken)
    {
        userDeviceId.Require();

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var idPrefix = ExternalContactId.GetFormatPrefix(userDeviceId);
        var dbExternalContacts = await dbContext.ExternalContacts
            .Where(a => a.Id.StartsWith(idPrefix)) // This is faster than index-based approach
            .OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.Version, x.Hash })
            .Take(MaxListCount + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (dbExternalContacts.Count > MaxListCount) {
            Log.LogWarning("List: {UserDeviceId} has over {MaxListCount} external contacts", userDeviceId, MaxListCount);
            dbExternalContacts.RemoveRange(MaxListCount, dbExternalContacts.Count - MaxListCount);
        }

        return dbExternalContacts.Select(x =>
                new ExternalContact(ExternalContactId.Parse(x.Id), x.Version) { Hash = new HashString(x.Hash) })
            .ToArray();
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

        var links = GetLinksFor(account);
        if (links.Length == 0)
            return "";

        var list = new List<ExternalContactId>();
        foreach (var link in links) {
            var extContactIds = await List(ownerId, link, cancellationToken).ConfigureAwait(false);
            list.AddRange(extContactIds);
        }
        if (list.Count == 0)
            return string.Empty;

        var extContacts = (await list
            .Select(c => Get(c, cancellationToken))
            .Collect(cancellationToken)
            .ConfigureAwait(false)
            ).ToArray();

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
        var idPrefix = ExternalContactId.GetFormatPrefix(ownerId);
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var externalContactIds = await dbContext.ExternalContactLinks
            .Where(x => x.DbExternalContactId.StartsWith(idPrefix))
            .Where(x => x.Value == link)
            .Select(x => x.DbExternalContactId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [..externalContactIds.Select(ExternalContactId.Parse)];
    }

    // Not compute method!
    public async Task<ApiSet<ExternalContactId>> ListReferencingContactIds(UserId userId, CancellationToken cancellationToken)
    {
        var account = await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
        if (account is null)
            return new ApiSet<ExternalContactId>();

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var links = GetLinksFor(account);
        // This grows with how many other address books reference this account, not with its own data
        var externalContactIds = await dbContext.ExternalContactLinks
            .Where(x => links.Contains(x.Value))
            .OrderBy(x => x.DbExternalContactId)
            .Select(x => x.DbExternalContactId)
            .Take(MaxListCount + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (externalContactIds.Count > MaxListCount) {
            Log.LogWarning("ListReferencingContactIds: {UserId} is referenced over {MaxListCount} times", userId, MaxListCount);
            externalContactIds.RemoveRange(MaxListCount, externalContactIds.Count - MaxListCount);
        }

        return [..externalContactIds.Select(ExternalContactId.Parse)];
    }

    private static ImmutableArray<string> GetLinksFor(AccountFull account)
    {
        var list = ImmutableArray<string>.Empty;
        foreach (var phoneHash in account.Identities.GetPhoneHashes())
            list = list.Add(DbExternalContactLink.GetPhoneLink(phoneHash));
        foreach (var emailHash in account.Identities.GetEmailHashes())
            list = list.Add(DbExternalContactLink.GetEmailLink(emailHash));
        return list;
    }

    // [CommandHandler]
    public virtual async Task<Result<ExternalContactFull?>[]> OnBulkChange(
        ExternalContactsBackend_BulkChange command,
        CancellationToken cancellationToken)
    {
        const string hashesItemKey = "ModifiedItemHashesKey";
        var context = CommandContext.GetCurrent();

        if (Invalidation.IsActive) {
            var invUserDeviceIds = command.Changes.Select(x => x.Id.UserDeviceId).Distinct();
            foreach (var invId in invUserDeviceIds)
                _ = List(invId, default);
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

        var overallAffectedHashes = new HashSet<string>();
        var updatedItemHashes = new List<IReadOnlyCollection<string>>();
        var result = new List<Result<ExternalContactFull?>>(command.Changes.Length);
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var someDeviceId = command.Changes.Select(c => c.Id.UserDeviceId).FirstOrDefault();
        await dbContext.ExternalContacts.Lock(someDeviceId, cancellationToken).ConfigureAwait(false);

        foreach (var itemChange in command.Changes)
            try {
                var changeResult = await ChangeItem(dbContext, itemChange, cancellationToken).ConfigureAwait(false);
                overallAffectedHashes.AddRange(changeResult.AffectedHashes);
                result.Add(new (changeResult.ExternalContactFull));
                if (itemChange.Change.Kind is ChangeKind.Remove or ChangeKind.Update && changeResult.AffectedHashes.Count > 0)
                    updatedItemHashes.Add(changeResult.AffectedHashes);
            }
            catch (Exception e) {
                Log.LogError(e,
                    "Failed to {ChangeKind} external contact #{ExternalContactId}",
                    itemChange.Change.Kind.ToString().ToLower(),
                    itemChange.Id);
                result.Add(new Result<ExternalContactFull?>(null, e));
            }

        context.Operation.Items.Set(hashesItemKey, overallAffectedHashes.ToList());
        if (updatedItemHashes.Count > 0) {
            var ownerId = command.Changes[0].Id.UserDeviceId.OwnerId;
            foreach (var itemHash in updatedItemHashes)
                context.Operation.AddEvent(new ExternalContactNameMayHaveChangedEvent(ownerId, itemHash.ToImmutableArray()));
        }

        return result.ToArray();
    }

    private async Task<ChangeItemResult> ChangeItem(
        ContactsDbContext dbContext,
        ExternalContactChange itemChange,
        CancellationToken cancellationToken)
    {
        var (id, expectedVersion, change) = itemChange;
        id.Require();
        change.RequireValid();

        // Can't use .ForUpdate() here due to join
        var dbExternalContact = await dbContext.ExternalContacts
            .Include(x => x.ExternalContactLinks)
            .FirstOrDefaultAsync(c => c.Id == id.Value, cancellationToken)
            .ConfigureAwait(false);
        var existing = dbExternalContact?.ToModel();
        var now = Clocks.SystemClock.Now;
        var modifiedItemHashes = new HashSet<string>();

        if (change.IsCreate(out var externalContact)) {
            if (existing != null)
                // Already exists, so we don't recreate one
                return new ChangeItemResult(existing, ImmutableArray<string>.Empty);
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
                return new ChangeItemResult(null, ImmutableArray<string>.Empty);
            dbExternalContact.RequireVersion(expectedVersion);
            dbContext.Remove(dbExternalContact);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false); // TODO(FC): bulk save
        var externalContactFull = dbExternalContact.ToModel();
        if (change.IsUpdate(out _))
            AddHashes(existing!);
        AddHashes(externalContactFull);
        return new ChangeItemResult(externalContactFull, modifiedItemHashes);

        void AddHashes(ExternalContactFull externalContact1) {
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

        var idPrefix = ExternalContactId.GetFormatPrefix(userId);
        // we remove contacts without invalidation since nobody else sees these contacts
        await dbContext.ExternalContacts
            .Where(a => a.Id.StartsWith(idPrefix)) // This is faster than index-based approach
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    // Nested types
    private record ChangeItemResult(
        ExternalContactFull? ExternalContactFull,
        IReadOnlyCollection<string> AffectedHashes);
}
