using ActualChat.Contacts.Db;
using ActualChat.Db;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Redis;
using ActualLab.Resilience;
using ActualLab.Versioning;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace ActualChat.Contacts;

/// <summary>
/// Backend service implementation for managing user contacts and chat memberships.
/// </summary>
public class ContactsBackend(IServiceProvider services) : DbServiceBase<ContactsDbContext>(services), IContactsBackend
{
    public static readonly TimeSpan GreetTimeout = TimeSpan.FromSeconds(20);

    private const string RedisKeyPrefix = ".ContactGreetingLocks.";

    private IAccountsBackend AccountsBackend => field ??= Services.GetRequiredService<IAccountsBackend>();
    private IAuthorsBackend AuthorsBackend => field ??= Services.GetRequiredService<IAuthorsBackend>();
    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();
    private IExternalContactsBackend ExternalContactsBackend
        => field ??= Services.GetRequiredService<IExternalContactsBackend>();
    private IDbEntityResolver<string, DbContact> DbContactResolver
        => field ??= Services.GetRequiredService<IDbEntityResolver<string, DbContact>>();
    private IDbEntityResolver<string, DbArchivedContact> DbArchivedContactResolver
        => field ??= Services.GetRequiredService<IDbEntityResolver<string, DbArchivedContact>>();
    private IMeshLocks GreetLocks => field ??= Services.MeshLocks().WithKeyPrefix(nameof(GreetLocks));
    public RedisDb<ContactsDbContext> RedisDb => field ??= Services.GetRequiredService<RedisDb<ContactsDbContext>>();
    private IDbEntityResolver<string, DbThreadContact> DbThreadContactResolver
        => field ??= Services.GetRequiredService<IDbEntityResolver<string, DbThreadContact>>();

    // [ComputeMethod]
    public virtual async Task<Contact> Get(UserId ownerId, ContactId contactId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ownerId);
        ArgumentNullException.ThrowIfNull(contactId.ChatId);
        ArgumentOutOfRangeException.ThrowIfNotEqual(ownerId, contactId.OwnerId);

        var dbContact = await DbContactResolver.Get(contactId.Value, cancellationToken).ConfigureAwait(false);
        // Blocked contacts live in the archive; a blocked row left here awaits BlockedContactsMigrationFlow
        if (dbContact is { State: ContactState.Blocked })
            dbContact = null;

        var contact = dbContact?.ToModel();
        if (contact is null) {
            var dbArchivedContact = await DbArchivedContactResolver.Get(contactId.Value, cancellationToken)
                .ConfigureAwait(false);
            contact = dbArchivedContact?.ToModel()
                ?? new Contact(contactId); // A fake contact
        }

        var chatId = contact.ChatId;
        if (chatId is PeerChatId peerChatId) {
            var userId = peerChatId.AnotherUserIdOrNull(ownerId);
            if (userId is null)
                throw new ArgumentOutOfRangeException(nameof(contactId));

            var account = await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
            var isBlockedByPeer = await IsBlocked(userId, ownerId, cancellationToken).ConfigureAwait(false);
            contact = contact with { Account = account.ToAccount(), IsBlockedByPeer = isBlockedByPeer };
        }

        if (contactId.ChatId != Constants.Chat.AnnouncementsChatId)
            await PseudoChatContact(contactId.ChatId).ConfigureAwait(false);

        return contact;
    }

    // [ComputeMethod]
    public virtual async Task<ContactId[]> ListIdsForSearch(UserId userId, ContactSubset contactSubset, bool includePublic, CancellationToken cancellationToken)
    {
        if (!contactSubset.IsAll())
            return await ListPlaceContactIds(userId, contactSubset.PlaceId, includePublic, cancellationToken).ConfigureAwait(false);

        var placeIds = await ListPlaceIds(userId, cancellationToken).ConfigureAwait(false);
        var contactIds = await placeIds.PrefixWith(null)
            .Select(id => ListPlaceContactIds(userId, id, includePublic, cancellationToken))
            .Collect(cancellationToken)
            .Flatten()
            .ConfigureAwait(false);
        return contactIds
            .Where(id => id.ChatId is not PlaceChatId { IsRoot: true })
            .ToArray();
    }

    // [ComputeMethod]
    public virtual async Task<ContactId[]> ListIdsForGroupContactSearch(UserId userId, ContactSubset contactSubset, CancellationToken cancellationToken)
    {
        var contactIds = await ListIdsForSearch(userId, contactSubset, true, cancellationToken).ConfigureAwait(false);
        return contactIds.Where(x => x.ChatId is { Kind: ChatKind.Group or ChatKind.Place })
            .ToArray();
    }

    // [ComputeMethod]
    public virtual async Task<ContactId[]> ListPeerContactIds(
        UserId userId,
        PlaceId? placeId,
        CancellationToken cancellationToken)
    {
        var contactIds = await ListIds(userId, null, cancellationToken).ConfigureAwait(false);
        var peerContactIds = contactIds.Where(x => x.ChatId.Kind == ChatKind.Peer).ToArray();
        if (placeId is null)
            return peerContactIds;

        var placeUserIds = await AuthorsBackend.ListPlaceUserIds(placeId, cancellationToken).ConfigureAwait(false);
        return peerContactIds
            .IntersectBy(placeUserIds, x => ((PeerChatId)x.ChatId).UserIds.OtherThan(userId))
            .ToArray();
    }

    // [ComputeMethod]
    public virtual async Task<ContactId[]> ListIds(UserId ownerId, PlaceId? placeId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ownerId);

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var idPrefix = ownerId.Value + ' ';
        var sPlaceId = placeId?.Id.Value.NullIfEmpty();
        var sContactIds = await dbContext.Contacts
            .Where(a => a.Id.StartsWith(idPrefix)) // This is faster than index-based approach
            .Where(a => a.PlaceId == sPlaceId)
            .Where(a => a.State != ContactState.Blocked)
            .OrderByDescending(a => a.TouchedAt)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var contactIds = sContactIds.Select(ContactId.Parse).ToList();

        if (placeId is null) {
            if (!contactIds.Exists(c => c.ChatId == Constants.Chat.AnnouncementsChatId))
                contactIds.Insert(0, ContactId.NewChat(ownerId, Constants.Chat.AnnouncementsChatId));
        }
        else {
            // Add all public chats from this place
            await PseudoPlaceContact(placeId).ConfigureAwait(false);
            var publicChatIds = await ChatsBackend
                .GetPublicChatIdsFor(placeId, cancellationToken)
                .ConfigureAwait(false);
            var addedChatIds = contactIds.Select(c => c.ChatId).ToHashSet();
            // An archived public chat stays out of the list too
            var sArchivedChatIds = await dbContext.ArchivedContacts
                .Where(a => a.OwnerId == ownerId.Value && a.PlaceId == sPlaceId)
                .Select(a => a.ChatId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            addedChatIds.UnionWith(sArchivedChatIds.Select(ChatId.Parse));
            var missingChatIds = publicChatIds.Where(c => !addedChatIds.Contains(c)).ToList();
            if (missingChatIds.Count != 0) {
                var contactsToAdd = missingChatIds.Select(c => ContactId.NewAny(ownerId, c)).ToList();
                contactIds.InsertRange(0, contactsToAdd);
            }
        }

        foreach (var contactId in contactIds) {
            if (contactId.ChatId != Constants.Chat.AnnouncementsChatId)
                await PseudoChatContact(contactId.ChatId).ConfigureAwait(false);
        }

        return contactIds.ToArray();
    }

    // [ComputeMethod]
    public virtual async Task<ContactId[]> ListBlockedIds(UserId ownerId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ownerId);

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var idPrefix = ownerId.Value + ' ';
        var sContactIds = await dbContext.ArchivedContacts
            .Where(a => a.Id.StartsWith(idPrefix)) // This is faster than index-based approach
            .Where(a => a.IsBlocked)
            .OrderByDescending(a => a.ArchivedAt)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return sContactIds.Select(ContactId.Parse).ToArray();
    }

    // [ComputeMethod]
    public virtual async Task<bool> IsBlocked(UserId ownerId, UserId otherUserId, CancellationToken cancellationToken)
    {
        if (ownerId == otherUserId)
            return false;
        if (ownerId.IsGuestOrNull() || otherUserId.IsGuestOrNull())
            return false;

        var contactId = ContactId.NewUser(ownerId, otherUserId);
        var dbArchivedContact = await DbArchivedContactResolver.Get(contactId.Value, cancellationToken)
            .ConfigureAwait(false);
        return dbArchivedContact?.IsBlocked == true;
    }

    // [ComputeMethod]
    public virtual async Task<int> GetArchivedCount(
        UserId ownerId, PlaceId? placeId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ownerId);

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var sPlaceId = placeId?.Id.Value.NullIfEmpty();
        return await dbContext.ArchivedContacts
            .Where(a => a.OwnerId == ownerId.Value && a.PlaceId == sPlaceId && !a.IsBlocked)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ContactId[]> ListArchivedIds(
        UserId ownerId, PlaceId? placeId, Tile<int> indexTile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ownerId);

        // Any change of the archive resets the count, and so every tile
        await GetArchivedCount(ownerId, placeId, cancellationToken).ConfigureAwait(false);

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var sPlaceId = placeId?.Id.Value.NullIfEmpty();
        var sContactIds = await dbContext.ArchivedContacts
            .Where(a => a.OwnerId == ownerId.Value && a.PlaceId == sPlaceId && !a.IsBlocked)
            .OrderByDescending(a => a.ArchivedAt)
            .ThenBy(a => a.Id)
            .Skip(indexTile.Start)
            .Take(indexTile.End - indexTile.Start)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return sContactIds.Select(ContactId.Parse).ToArray();
    }

    // [ComputeMethod]
    public virtual async Task<PlaceId[]> ListPlaceIds(UserId ownerId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ownerId);

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var idPrefix = ownerId.Value + ' ';
        var sPlaceIds = await dbContext.PlaceContacts
            .Where(a => a.Id.StartsWith(idPrefix)) // This is faster than index-based approach
            .OrderBy(a => a.Id)
            .Select(a => a.PlaceId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var placeIds = sPlaceIds.Select(PlaceId.Parse).ToArray();
        foreach (var placeId in placeIds)
            await PseudoPlaceContact(placeId).ConfigureAwait(false);
        return placeIds;
    }

    // [ComputeMethod]
    public virtual async Task<ThreadContact?> GetThreadContact(
        UserId ownerId,
        ContactId contactId,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(ownerId, contactId.OwnerId);

        var dbThreadContact = await DbThreadContactResolver.Get(contactId.Value, cancellationToken).ConfigureAwait(false);
        return dbThreadContact?.ToModel();
    }

    // [ComputeMethod]
    public virtual async Task<ThreadChatId[]> ListThreadIdsForChat(
        UserId ownerId,
        ChatId parentChatId,
        CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        var idPrefix = ownerId.Value + ' ' + parentChatId.Value + ChatId.ThreadIdSeparator;
        var sChatIds = await dbContext.ThreadContacts
            .Where(a => a.Id.StartsWith(idPrefix)) // This is faster than index-based approach
            .OrderByDescending(a => a.IsPinned)
            .ThenByDescending(a => a.TouchedAt)
            .Select(a => a.ThreadChatId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return sChatIds
            .Select(ChatId.ParseNullable)
            .SkipNullItems()
            .Where(c => c.IsThread())
            .Cast<ThreadChatId>()
            .ToArray();
    }

    // [ComputeMethod]
    public virtual async Task<ThreadChatId[]> ListThreadIdsForPlace(
        UserId ownerId,
        PlaceId? parentPlaceId,
        CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        IQueryable<DbThreadContact> dbThreadContacts;
        if (parentPlaceId is not null) {
            var idPrefix = ownerId.Value + ' ' + PlaceChatId.Format(parentPlaceId, Symbol.Empty);
            dbThreadContacts = dbContext.ThreadContacts
                .Where(a => a.Id.StartsWith(idPrefix)); // This is faster than index-based approach
        }
        else {
            var idPrefix = ownerId.Value + ' ';
            var excludeIdPrefix = idPrefix + PlaceChatId.IdPrefix;
            dbThreadContacts = dbContext.ThreadContacts
                .Where(a => a.Id.StartsWith(idPrefix)) // This is faster than index-based approach
                .Where(a => !a.Id.StartsWith(excludeIdPrefix))
                .Where(a => a.PlaceId == "");
        }
        var sChatIds = await dbThreadContacts
            .OrderByDescending(a => a.IsPinned)
            .ThenByDescending(a => a.TouchedAt)
            .Select(a => a.ThreadChatId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return sChatIds
            .Select(ChatId.ParseNullable)
            .SkipNullItems()
            .Where(c => c.IsThread())
            .Cast<ThreadChatId>()
            .ToArray();
    }

    // Not a [ComputeMethod]!
    public async Task<Contact[]> ListChangedPeerContacts(ChangedContactsQuery query, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var chatsQuery = query.LastId == null
            ? dbContext.Contacts.Where(x => x.Version >= query.MinVersion && x.Version <= query.MaxVersion)
            : dbContext.Contacts.Where(x => (x.Version > query.MinVersion && x.Version <= query.MaxVersion)
                || (x.Version == query.MinVersion && string.Compare(x.Id, query.LastId.Value) > 0));

        var dbContacts = await chatsQuery
            .Where(x => x.UserId != null && !Constants.User.SystemUserIdValues.Contains(x.UserId!))
            .OrderBy(x => x.Version)
            .ThenBy(x => x.Id)
            .Take(query.Limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return dbContacts.Select(x => x.ToModel()).ToArray();
    }

    // Not a [ComputeMethod]!
    public async Task<Contact[]> ListBlockedForMigration(
        string? lastId, int limit, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var query = dbContext.Contacts.Where(x => x.State == ContactState.Blocked);
        if (!lastId.IsNullOrEmpty())
            query = query.Where(x => string.Compare(x.Id, lastId) > 0);

        var dbContacts = await query
            .OrderBy(x => x.Id)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return dbContacts.Select(x => x.ToModel()).ToArray();
    }

    // Not a [ComputeMethod]!
    public async Task<Contact[]> ListChangedPlaceContacts(
        ChangedContactsQuery query, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var lastSid = query.LastId != null
            ? DbPlaceContact.FormatId(query.LastId)
            : null;
        var placeContactsQuery = query.LastId == null
            ? dbContext.PlaceContacts.Where(x => x.Version >= query.MinVersion && x.Version <= query.MaxVersion)
            : dbContext.PlaceContacts.Where(x => (x.Version > query.MinVersion && x.Version <= query.MaxVersion)
                || (x.Version == query.MinVersion && string.Compare(x.Id, lastSid) > 0));

        var dbContacts = await placeContactsQuery
            .OrderBy(x => x.Version)
            .ThenBy(x => x.Id)
            .Take(query.Limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return dbContacts.Select(x => x.ToModel()).ToArray();
    }

    // [CommandHandler]
    public virtual async Task<Contact?> OnChange(
        ContactsBackend_Change command,
        CancellationToken cancellationToken)
    {
        var (id, expectedVersion, change) = command;
        var ownerId = id.OwnerId;
        var chatId = id.ChatId;
        var placeId = (chatId as PlaceChatId)?.PlaceId;
        var context = CommandContext.GetCurrent();

        id.Require();
        ownerId.Require();
        change.RequireValid();

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        await dbContext.Contacts.Lock(id, cancellationToken).ConfigureAwait(false);
        var dbContact = await dbContext.Contacts
            .FirstOrDefaultAsync(c => c.Id == id.Value, cancellationToken)
            .ConfigureAwait(false);
        var existing = dbContact?.ToModel();
        var dbArchivedContact = dbContact is null
            ? await dbContext.ArchivedContacts
                .FirstOrDefaultAsync(c => c.Id == id.Value, cancellationToken)
                .ConfigureAwait(false)
            : null;

        if (change.IsCreate(out var contact)) {
            if (contact.State == ContactState.Blocked)
                throw StandardError.Constraint("Blocking is managed by the SetIsBlocked command.");

            if (dbArchivedContact != null) {
                // Archived contacts are brought back by Unarchive only
                return dbArchivedContact.ToModel();
            }

            if (dbContact != null) {
                var existingContact = existing.Require();
                // The only state change Create can cause is reviving a Temporary contact to Regular
                if (contact.State != ContactState.Regular || existingContact.State != ContactState.Temporary)
                    return existingContact; // Already exists, so we don't recreate one

                contact = existingContact with {
                    Version = VersionGenerator.NextVersion(dbContact.Version),
                    State = ContactState.Regular,
                    TouchedAt = Clocks.SystemClock.Now,
                };
                dbContact.UpdateFrom(contact);
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                DeferInvalidation();
                context.Operation.AddEvent(new ContactChangedEvent(contact, existing, ChangeKind.Update));
                return contact;
            }

            // Original UserId is ignored here - it's set based on Id
            var userId = id.ChatId is PeerChatId peerChatId
                ? peerChatId.UserIds.OtherThan(ownerId)
                : null;

            // Checks
            if (ownerId.IsGuest && userId != null)
                throw StandardError.Constraint("You must sign-in to chat with another user.");
            if (userId?.IsGuest == true)
                throw StandardError.Constraint("You can't chat with unauthenticated user.");

            contact = contact with {
                Id = id,
                Version = VersionGenerator.NextVersion(),
                UserId = userId,
                TouchedAt = Clocks.SystemClock.Now,
            };
            dbContact = new DbContact(contact);
            dbContext.Add(dbContact);
        }
        else if (change.IsUpdate(out contact)) {
            if (dbArchivedContact != null) {
                // Archiving and blocking own the rest of an archived contact
                dbArchivedContact.RequireVersion(expectedVersion);
                dbArchivedContact.PeerContactName = contact.PeerContactName;
                dbArchivedContact.ExternalContactName = contact.ExternalContactName;
                dbArchivedContact.IsPinned = contact.IsPinned;
                dbArchivedContact.Version = VersionGenerator.NextVersion(dbArchivedContact.Version);
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                DeferArchiveInvalidation(ownerId, id);
                return dbArchivedContact.ToModel();
            }

            dbContact.RequireVersion(expectedVersion);
            // Blocking is owned by the SetIsBlocked command, which keeps blocked contacts in the archive
            var state = contact.State == ContactState.Blocked ? existing!.State : contact.State;
            contact = contact with {
                Version = VersionGenerator.NextVersion(dbContact.Version),
                State = state,
            };
            dbContact.UpdateFrom(contact);
        }
        else { // Remove
            if (dbContact == null) {
                if (dbArchivedContact == null) {
                    if (expectedVersion != null)
                        dbContact.RequireVersion(expectedVersion);

                    return null;
                }

                if (expectedVersion != null)
                    dbArchivedContact.RequireVersion(expectedVersion);
                var removedContact = dbArchivedContact.ToModel();
                dbContext.Remove(dbArchivedContact);
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                DeferArchiveInvalidation(ownerId, id);
                // It's not in the contact list, so the event has no old contact: nothing but the index cares
                context.Operation.AddEvent(new ContactChangedEvent(removedContact, null, ChangeKind.Remove));
                return removedContact;
            }

            if (expectedVersion != null)
                dbContact.RequireVersion(expectedVersion);

            dbContext.Remove(dbContact);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        DeferInvalidation();
        contact = dbContact.ToModel();
        context.Operation.AddEvent(new ContactChangedEvent(contact, existing, change.Kind));
        return contact;

        void DeferInvalidation()
            => Invalidation.Defer(() => {
                _ = Get(ownerId, id, default);
                _ = ListIds(ownerId, placeId, default);
                if (chatId is PeerChatId invPeerChatId) {
                    var otherUserId = invPeerChatId.AnotherUserIdOrNull(ownerId);
                    if (otherUserId is not null) {
                        _ = IsBlocked(ownerId, otherUserId, default);
                        // Other side's IsBlockedByPeer depends on ownerId's IsBlocked
                        _ = Get(otherUserId, ContactId.NewUser(otherUserId, ownerId), default);
                    }
                    _ = ListBlockedIds(ownerId, default);
                }
            });
    }

    // [CommandHandler]
    public virtual async Task OnTouch(ContactsBackend_Touch command, CancellationToken cancellationToken)
    {
        var id = command.Id;
        var ownerId = id.OwnerId;
        var chatId = id.ChatId;
        var placeId = (chatId as PlaceChatId)?.PlaceId;

        var contactIds = await ListIds(ownerId, placeId, cancellationToken).ConfigureAwait(false);

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbContact = await dbContext.Contacts.ForUpdate()
            .FirstOrDefaultAsync(c => c.Id == id.Value, cancellationToken)
            .ConfigureAwait(false);
        if (dbContact == null)
            return;

        var contact = dbContact.ToModel();
        contact = contact with {
            Version = VersionGenerator.NextVersion(contact.Version),
            TouchedAt = Clocks.SystemClock.Now,
        };
        dbContact.UpdateFrom(contact);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var index = (long)contactIds.IndexOf(id);
        Invalidation.Defer(() => {
            _ = Get(ownerId, id, default);
            // Contacts are sorted by TouchedAt, and we load contacts in 2 stages: the 1st is limited by MinLoadLimit,
            // hence we need to invalidate ListIds for Update only in case it was not in MinLoadList before the change.
            if (index < 0 || index > Constants.Contacts.MinLoadLimit)
                _ = ListIds(ownerId, placeId, default); // Create, Delete or move into MinLoadLimit
        });
    }

    // [CommandHandler]
    public virtual async Task OnSetIsBlocked(ContactsBackend_SetIsBlocked command, CancellationToken cancellationToken)
    {
        var (id, isBlocked) = command;
        id.Require();
        if (id.ChatId is not PeerChatId)
            throw StandardError.Constraint("Only peer contacts can be blocked.");

        if (isBlocked) {
            await ArchiveContact(id, true, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Unblocking leaves the contact archived: it is brought back by Unarchive only
        var ownerId = id.OwnerId;
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        await dbContext.Contacts.Lock(id, cancellationToken).ConfigureAwait(false);
        var dbArchivedContact = await dbContext.ArchivedContacts
            .FirstOrDefaultAsync(c => c.Id == id.Value, cancellationToken)
            .ConfigureAwait(false);
        if (dbArchivedContact is not { IsBlocked: true })
            return;

        dbArchivedContact.IsBlocked = false;
        dbArchivedContact.Version = VersionGenerator.NextVersion(dbArchivedContact.Version);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        DeferArchiveInvalidation(ownerId, id);
    }

    // [CommandHandler]
    public virtual Task OnArchive(ContactsBackend_Archive command, CancellationToken cancellationToken)
        => ArchiveContact(command.Id, false, cancellationToken);

    // [CommandHandler]
    public virtual async Task OnUnarchive(ContactsBackend_Unarchive command, CancellationToken cancellationToken)
    {
        var id = command.Id;
        id.Require();

        var ownerId = id.OwnerId;
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        await dbContext.Contacts.Lock(id, cancellationToken).ConfigureAwait(false);
        var dbArchivedContact = await dbContext.ArchivedContacts
            .FirstOrDefaultAsync(c => c.Id == id.Value, cancellationToken)
            .ConfigureAwait(false);
        if (dbArchivedContact == null)
            return;
        if (dbArchivedContact.IsBlocked)
            throw StandardError.Constraint("Unblock the contact to unarchive it.");

        var dbContact = await dbContext.Contacts
            .FirstOrDefaultAsync(c => c.Id == id.Value, cancellationToken)
            .ConfigureAwait(false);
        Contact? restoredContact = null;
        if (dbContact == null) {
            // The contact goes to the top of the list, as if it was just touched
            restoredContact = dbArchivedContact.ToModel() with {
                Version = VersionGenerator.NextVersion(dbArchivedContact.Version),
                TouchedAt = Clocks.SystemClock.Now,
                ArchivedAt = null,
            };
            dbContext.Add(new DbContact(restoredContact));
        }
        dbContext.Remove(dbArchivedContact);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        DeferArchiveInvalidation(ownerId, id);
        if (restoredContact != null) {
            var context = CommandContext.GetCurrent();
            context.Operation.AddEvent(new ContactChangedEvent(restoredContact, null, ChangeKind.Create));
        }
    }

    // [CommandHandler]
    public virtual async Task OnMigrateBlocked(
        ContactsBackend_MigrateBlocked command, CancellationToken cancellationToken)
    {
        var id = command.Id;
        id.Require();

        var ownerId = id.OwnerId;
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        await dbContext.Contacts.Lock(id, cancellationToken).ConfigureAwait(false);
        var dbContact = await dbContext.Contacts
            .FirstOrDefaultAsync(c => c.Id == id.Value && c.State == ContactState.Blocked, cancellationToken)
            .ConfigureAwait(false);
        if (dbContact == null)
            return;

        var dbArchivedContact = await dbContext.ArchivedContacts
            .FirstOrDefaultAsync(c => c.Id == id.Value, cancellationToken)
            .ConfigureAwait(false);
        if (dbArchivedContact == null) {
            var version = VersionGenerator.NextVersion(dbContact.Version);
            dbContext.Add(new DbArchivedContact(dbContact.ToModel(), Clocks.SystemClock.Now, version));
        }
        else if (!dbArchivedContact.IsBlocked) {
            dbArchivedContact.IsBlocked = true;
            dbArchivedContact.Version = VersionGenerator.NextVersion(dbArchivedContact.Version);
        }
        dbContext.Remove(dbContact);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        DeferArchiveInvalidation(ownerId, id);
        // A blocked contact is not a friend, so only the search index has something to drop
        var context = CommandContext.GetCurrent();
        context.Operation.AddEvent(new ContactChangedEvent(dbContact.ToModel(), null, ChangeKind.Remove));
    }

    // [CommandHandler]
    public virtual async Task OnRemoveAccount(ContactsBackend_RemoveAccount command, CancellationToken cancellationToken)
    {
        var userId = command.UserId;
        // var contactIds = await ListIds(userId, cancellationToken).ConfigureAwait(false);

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var idPrefix = userId.Value + ' ';
        await dbContext.Contacts
            .Where(a => a.Id.StartsWith(idPrefix)) // This is faster than index-based approach
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
        await dbContext.ArchivedContacts
            .Where(a => a.Id.StartsWith(idPrefix))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        var contactIds = await dbContext.Contacts
            .Where(a => a.UserId == userId.Value)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var archivedContactIds = await dbContext.ArchivedContacts
            .Where(a => a.UserId == userId.Value)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        contactIds.AddRange(archivedContactIds);

        foreach (var contactId in contactIds) {
            var removeCommand = new ContactsBackend_Change(
                ContactId.Parse(contactId), null,
                new Change<Contact> { Remove = true });
            await Commander.Call(removeCommand, cancellationToken).ConfigureAwait(false);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnRemoveChatContacts(ContactsBackend_RemoveChatContacts command, CancellationToken cancellationToken)
    {
        var chatId = command.ChatId;

        if (chatId is PlaceChatId { IsRoot: true } placeChatId) {
            var placeId = placeChatId.PlaceId;

            var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
            await using var __ = dbContext.ConfigureAwait(false);

            await dbContext.PlaceContacts
                .Where(c => c.PlaceId == placeId.Value)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            Invalidation.Defer(() => _ = PseudoPlaceContact(placeId));
        }
        else {
            var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
            await using var __ = dbContext.ConfigureAwait(false);

            await dbContext.Contacts
                .Where(c => c.ChatId == chatId.Value)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            var archivedIds = await dbContext.ArchivedContacts
                .Where(c => c.ChatId == chatId.Value)
                .Select(c => c.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (archivedIds.Count != 0)
                await dbContext.ArchivedContacts
                    .Where(c => c.ChatId == chatId.Value)
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            Invalidation.Defer(() => {
                _ = PseudoChatContact(chatId);
                foreach (var archivedId in archivedIds) {
                    var archivedContactId = ContactId.Parse(archivedId);
                    InvalidateArchive(archivedContactId.OwnerId, archivedContactId);
                }
            });
        }
    }

    // [CommandHandler]
    public virtual async Task OnGreet(ContactsBackend_Greet command, CancellationToken cancellationToken)
    {
        var account = await AccountsBackend.Get(command.UserId, cancellationToken).ConfigureAwait(false);
        if (account is null || account.IsGreetingCompleted || account.IsBot)
            return;

        var h = await GreetLocks.TryLock(account.Id.Value, null, cancellationToken).ConfigureAwait(false);
        if (h == null)
            return; // Another host is already greeting
        await using var _ = h.ConfigureAwait(false);

        var alreadyGreetingKey = ToRedisKey(account.Id);
        var database = await RedisDb.Database.Get(cancellationToken).ConfigureAwait(false);
        var canStart = await database.StringSetAsync(
            alreadyGreetingKey,
            Clocks.SystemClock.Now.ToString(),
            GreetTimeout,
            When.NotExists
            ).ConfigureAwait(false);
        if (!canStart)
            return;

        try {
            var referencingExtContactIds = await ExternalContactsBackend.ListReferencingContactIds(account.Id, cancellationToken)
                .ConfigureAwait(false);
            await referencingExtContactIds.Select(c => new { UserId =  c.UserDeviceId.OwnerId, ExtContactId = c })
                .Where(d => d.UserId != account.Id)
                .GroupBy(d => d.UserId)
                .Select(d => CreateContact(d.Key, d.Select(c => c.ExtContactId).ToList()))
                .Collect(cancellationToken)
                .ConfigureAwait(false);
            var completeCmd = new AccountsBackend_Update(account with { IsGreetingCompleted = true }, account.Version);
            try {
                await Commander.Call(completeCmd, true, cancellationToken).ConfigureAwait(false);
            }
            catch (VersionMismatchException) {
                // A passkey change rewrites the account's identities and moves its version, and this command
                // carries the whole account - a stale one would write those identities back. ContactGreeter
                // re-picks accounts with IsGreetingCompleted == false, so the next pass redoes this greeting.
                Log.LogWarning("Greet({UserId}): the account changed under us, retrying on the next pass",
                    account.Id);
            }
        }
        finally {
            await database.KeyDeleteAsync(alreadyGreetingKey).ConfigureAwait(false);
        }
        return;

        async Task<Contact?> CreateContact(UserId ownerId, IEnumerable<ExternalContactId> extContactIds) {
            string externalContactName = "";
            foreach (var externalContactId in extContactIds) {
                var externalContact = await ExternalContactsBackend.Get(externalContactId, cancellationToken).ConfigureAwait(false);
                if (externalContact is not null && !externalContact.DisplayName.IsNullOrEmpty()) {
                    externalContactName = externalContact.DisplayName;
                    break;
                }
            }
            var contact = new Contact(ContactId.NewUser(ownerId, account.Id)) {
                ExternalContactName = externalContactName,
                State = ContactState.Regular,
            };
            var cmd = new ContactsBackend_Change(contact.Id, null, Change.Create(contact));
            return await Commander.Call(cmd, true, cancellationToken).ConfigureAwait(false);
        }
    }

    // [CommandHandler]
    public virtual async Task OnChangePlaceMembership(
        ContactsBackend_ChangePlaceMembership command,
        CancellationToken cancellationToken)
    {
        var (placeId, ownerId, hasLeft) = command;
        var context = CommandContext.GetCurrent();

        ownerId.Require();
        placeId.Require();

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var id = DbPlaceContact.FormatId(ownerId.Value, placeId.Value);
        var dbPlaceContact = await dbContext.PlaceContacts.ForUpdate()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            .ConfigureAwait(false);
        var hasChanges = false;
        if (dbPlaceContact != null) {
            if (hasLeft) {
                dbContext.Remove(dbPlaceContact);
                hasChanges = true;
            }
        }
        else {
            if (!hasLeft) {
                var newDbPlaceContact = new DbPlaceContact(ownerId.Value, placeId.Value) {
                    Version = VersionGenerator.NextVersion(),
                };
                dbContext.Add(newDbPlaceContact);
                hasChanges = true;
            }
        }
        if (hasChanges) {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            Invalidation.Defer(() => _ = ListPlaceIds(ownerId, default));
            context.Operation.AddEvent(new PlaceMembershipChangedEvent(ownerId, placeId, hasLeft));
        }
    }

    // [CommandHandler]
    public virtual async Task OnPublishCopiedChat(
        ContactsBackend_PublishCopiedChat command,
        CancellationToken cancellationToken)
    {
        var chatId = command.ChatId;
        var placeId = chatId.PlaceId;

        Log.LogInformation("-> OnPublishCopiedChat: creating contacts for chat '{ChatId}'", chatId);

        var authorIds = await AuthorsBackend.ListAuthorIds(chatId, cancellationToken).ConfigureAwait(false);
        foreach (var authorId in authorIds) {
            var author = await AuthorsBackend.Get(authorId.ChatId, authorId, RequestedAuthorKind.Full, cancellationToken).ConfigureAwait(false);
            if (author == null || author.HasLeft)
                continue;

            var userId = author.UserId;
            var changePlaceMembership = new ContactsBackend_ChangePlaceMembership(placeId, userId, false);
            await Commander.Call(changePlaceMembership, false, cancellationToken).ConfigureAwait(false);

            var contactId = ContactId.NewChat(userId, chatId);
            var contact = await Get(userId, contactId, cancellationToken).ConfigureAwait(false);
            if (contact.IsRegular)
                continue; // No need to make any changes

            var createContact = new ContactsBackend_Change(
                contactId,
                contact.HasVersion() ? contact.Version : null,
                Change.Upsert(contact with { State = ContactState.Regular }));
            await Commander.Call(createContact, false, cancellationToken).ConfigureAwait(false);
        }

        Invalidation.Defer(() => {
            _ = PseudoChatContact(chatId);
            _ = PseudoPlaceContact(placeId);
        });

        Log.LogInformation("<- OnPublishCopiedChat: created contacts for chat '{ChatId}'", chatId);
    }

    // [CommandHandler]
    public virtual async Task OnReviewExternalContactName(
        ContactsBackend_ReviewExternalContactName command,
        CancellationToken cancellationToken)
    {
        var contactId = command.Id;
        var ownerUserId = contactId.OwnerId;
        var contact = await Get(ownerUserId, contactId, cancellationToken).ConfigureAwait(false);
        if (!contact.HasVersion())
            return;

        var peerUserId = contact.Account?.Id;
        if (peerUserId is null)
            return;

        var externalContactName = await ExternalContactsBackend
            .GetDisplayNameFor(ownerUserId, peerUserId, cancellationToken)
            .ConfigureAwait(false);
        if (contact.ExternalContactName == externalContactName)
            return;

        try {
            var change = Change.Upsert(contact with { ExternalContactName = externalContactName });
            var cmd = new ContactsBackend_Change(contactId, contact.Version, change);
            await Commander.Call(cmd, false, cancellationToken).ConfigureAwait(false);
        }
        catch (VersionMismatchException) {
            // NOTE: It would be better to reprocess the command right here without throwing TransientException.
            // Because not it clutters the log.
            throw new TransientException("Need to reprocess VersionMismatchException on ContactsBackend_Change");
        }
    }

    // [CommandHandler]
    public virtual async Task<ThreadContact?> OnChangeThreadContact(
        ContactsBackend_ChangeThreadContact command,
        CancellationToken cancellationToken)
    {
        var (id, expectedVersion, change) = command;
        var ownerId = id.OwnerId;

        id.Require();
        id.ChatId.IsThread().RequireTrue("Id.ChatId.IsThread must be true.");
        ownerId.Require();
        change.RequireValid();

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);
        await dbContext.ThreadContacts.Lock(id, cancellationToken).ConfigureAwait(false);

        var dbThreadContact = await dbContext.ThreadContacts
            .FirstOrDefaultAsync(c => c.Id == id.Value, cancellationToken)
            .ConfigureAwait(false);

        if (change.IsCreate(out var threadContact)) {
            if (dbThreadContact != null)
                return dbThreadContact.ToModel(); // Already exists, so we don't recreate one

            // Checks
            if (ownerId.IsGuest)
                throw StandardError.Constraint("You must sign-in to follow chat thread.");

            threadContact = threadContact with {
                Id = id,
                Version = VersionGenerator.NextVersion(),
                TouchedAt = Clocks.SystemClock.Now,
            };
            dbThreadContact = new DbThreadContact(threadContact);
            dbContext.Add(dbThreadContact);
        }
        else if (change.IsUpdate(out threadContact)) {
            dbThreadContact.RequireVersion(expectedVersion);
            threadContact = threadContact with {
                Version = VersionGenerator.NextVersion(dbThreadContact.Version),
            };
            dbThreadContact.UpdateFrom(threadContact);
        }
        else { // Remove
            if (expectedVersion != null)
                dbThreadContact.RequireVersion(expectedVersion);
            if (dbThreadContact == null)
                return null;

            dbContext.Remove(dbThreadContact);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        threadContact = dbThreadContact.ToModel();
        Invalidation.Defer(() => {
            _ = GetThreadContact(ownerId, id, default);
            var threadChatId = (ThreadChatId)id.ChatId;
            ThreadChatId? invChatId = threadChatId;
            while (invChatId is not null) {
                _ = ListThreadIdsForChat(ownerId, invChatId.ParentChatId, default);
                invChatId = invChatId.ParentChatId as ThreadChatId;
            }
            var parentChat = threadChatId.GetOutermostParent();
            var parentPlaceId = parentChat is PlaceChatId placeChatId ? placeChatId.PlaceId : null;
            _ = ListThreadIdsForPlace(ownerId, parentPlaceId, default);
        });
        return threadContact;
    }

    // Events

    // [EventHandler]
    public virtual async Task OnChatChangedEvent(ChatChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        var (chat, oldChat, changeKind) = eventCommand;
        // Thread contacts are created elsewhere: the starter's by ChatThreads.OnStart (threads have no
        // roles to find an owner in), everyone else's when they post or get mentioned.
        // TODO: remove thread contacts when a thread is removed
        if (chat.Id.IsThread())
            return;

        if (changeKind == ChangeKind.Remove) {
            var command = new ContactsBackend_RemoveChatContacts(chat.Id);
            await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);
        }
        else if (changeKind == ChangeKind.Update) {
            if (chat.IsFrozen && oldChat?.IsFrozen != true) {
                // Chat has been frozen, remove contacts to it.
                var command = new ContactsBackend_RemoveChatContacts(chat.Id);
                await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    // [EventHandler]
    public virtual async Task OnAuthorChangedEvent(AuthorUpsertedEvent eventCommand, CancellationToken cancellationToken)
    {
        var (author, oldAuthor) = eventCommand;
        var oldHasLeft = oldAuthor?.HasLeft ?? true;
        if (oldHasLeft == author.HasLeft && (oldAuthor?.Version ?? 0) != 0)
            return;

        var chatId = author.ChatId;
        var userId = author.UserId;
        if (chatId.Kind == ChatKind.Peer && author.HasLeft) // Users can't leave peer chats
            return;

        if (chatId is PlaceChatId { IsRoot: true } placeChatId) {
            var changePlaceMembership = new ContactsBackend_ChangePlaceMembership(placeChatId.PlaceId, userId, author.HasLeft);
            await Commander.Call(changePlaceMembership, true, cancellationToken).ConfigureAwait(false);
            return;
        }

        var contactId = ContactId.NewAny(userId, chatId);
        var contact = await Get(userId, contactId, cancellationToken).ConfigureAwait(false);
        if (contact.HasVersion() == !author.HasLeft)
            return; // No need to make any changes

        if (author.HasLeft) {
            var removeCommand = new ContactsBackend_Change(contactId, null, Change.Remove<Contact>());
            await Commander.Call(removeCommand, true, cancellationToken).ConfigureAwait(false);
            return;
        }

        var contactState = chatId.Kind == ChatKind.Peer ? ContactState.Temporary : ContactState.Regular;
        var change = new Change<Contact> {
            Create = new Contact(contactId) { State = contactState },
        };
        var command = new ContactsBackend_Change(contactId, null, change);
        await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);
    }

    // [EventHandler]
    public virtual async Task OnChatEntryChangedEvent(ChatEntryChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        var (entry, author, changeKind, _) = eventCommand;
        if (changeKind == ChangeKind.Remove || entry.IsSystemEntry)
            return;

        var userId = author.UserId;
        var chatId = entry.ChatId;

        if (!chatId.IsThread(out var threadChatId)) {
            var contactId = ContactId.NewAny(userId, chatId);
            var contact = await Get(userId, contactId, cancellationToken).ConfigureAwait(false);
            var now = Clocks.SystemClock.Now;
            if (now - contact.TouchedAt < Constants.Contacts.MinTouchInterval)
                return;

            var command = new ContactsBackend_Touch(contact.Id);
            await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);
        }
        else if (changeKind is ChangeKind.Create)
            await EnsureThreadContactExits(userId, threadChatId, cancellationToken).ConfigureAwait(false);
    }

    // [EventHandler]
    public virtual async Task OnExternalContactNameMayHaveChangedEvent(
        ExternalContactNameMayHaveChangedEvent eventCommand,
        CancellationToken cancellationToken)
    {
        var (ownerUserId, links) = eventCommand;
        var peerUserIds = new List<UserId>();
        foreach (var link in links.OrderBy(c => c)) {
            var peerUserId = await ExternalContactExt2.FindUser(AccountsBackend, link, cancellationToken).ConfigureAwait(false);
            if (peerUserId is not null && !peerUserIds.Contains(peerUserId))
                peerUserIds.Add(peerUserId);
        }

        foreach (var peerUserId in peerUserIds)  {
            if (peerUserId == ownerUserId)
                continue;

            var contactId = ContactId.NewUser(ownerUserId, peerUserId);
            var cmd = new ContactsBackend_ReviewExternalContactName(contactId);
            await Commander.Call(cmd, true, cancellationToken).ConfigureAwait(false);
        }
    }

    // [EventHandler]
    public virtual async Task OnUserMentionedInThreadChatEvent(
        UserMentionedInThreadChatEvent eventCommand,
        CancellationToken cancellationToken)
    {
        var (threadChatId, mentionIds) = eventCommand;
        var parentChat = threadChatId.GetOutermostParent();
        foreach (var mentionId in mentionIds) {
            AuthorFull? author = null;
            if (mentionId.Target is AuthorId authorId)
                author = await AuthorsBackend
                    .Get(parentChat, authorId, RequestedAuthorKind.Full, cancellationToken)
                    .ConfigureAwait(false);
            else if (mentionId.Target is UserId userId)
                author = await AuthorsBackend
                    .GetByUserId(parentChat, userId, RequestedAuthorKind.Full, cancellationToken)
                    .ConfigureAwait(false);
            if (author is null)
                continue;

            await EnsureThreadContactExits(author.UserId, threadChatId, cancellationToken).ConfigureAwait(false);
        }
    }

    // Protected methods

    [ComputeMethod]
    protected virtual async Task<ContactId[]> ListPlaceContactIds(
        UserId userId, PlaceId? placeId, bool includePublic, CancellationToken cancellationToken)
    {
        var contactIds = await ListIds(userId, placeId, cancellationToken).ConfigureAwait(false);
        if (includePublic)
            return contactIds;

        var publicChatIds = await ChatsBackend.GetPublicChatIdsFor(placeId, cancellationToken).ConfigureAwait(false);
        return contactIds.ExceptBy(publicChatIds, x => x.ChatId).ToArray();
    }

    [ComputeMethod]
    protected virtual Task<Unit> PseudoPlaceContact(PlaceId placeId)
        => ActualLab.Async.TaskExt.UnitTask;

    [ComputeMethod]
    protected virtual Task<Unit> PseudoChatContact(ChatId chatId)
        => ActualLab.Async.TaskExt.UnitTask;

    // Private methods

    private static string ToRedisKey(UserId userId)
        => $"{RedisKeyPrefix}{userId.Value}";

    private async Task ArchiveContact(ContactId id, bool isBlocked, CancellationToken cancellationToken)
    {
        id.Require();
        var ownerId = id.OwnerId;
        var chatId = id.ChatId;
        if (chatId is PlaceChatId { IsRoot: true } || Constants.Chat.SystemChatIds.Contains(chatId))
            throw StandardError.Constraint("This chat can't be archived.");

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        await dbContext.Contacts.Lock(id, cancellationToken).ConfigureAwait(false);
        var dbArchivedContact = await dbContext.ArchivedContacts
            .FirstOrDefaultAsync(c => c.Id == id.Value, cancellationToken)
            .ConfigureAwait(false);
        if (dbArchivedContact != null) {
            if (!isBlocked || dbArchivedContact.IsBlocked)
                return;

            dbArchivedContact.IsBlocked = true;
            dbArchivedContact.Version = VersionGenerator.NextVersion(dbArchivedContact.Version);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            DeferArchiveInvalidation(ownerId, id);
            return;
        }

        var dbContact = await dbContext.Contacts
            .FirstOrDefaultAsync(c => c.Id == id.Value, cancellationToken)
            .ConfigureAwait(false);
        var existing = dbContact?.ToModel();
        var contact = existing;
        if (contact == null) {
            var userId = chatId is PeerChatId peerChatId ? peerChatId.UserIds.OtherThan(ownerId) : null;
            if (ownerId.IsGuest && userId != null)
                throw StandardError.Constraint("You must sign-in to chat with another user.");
            if (userId?.IsGuest == true)
                throw StandardError.Constraint("You can't chat with unauthenticated user.");

            contact = new Contact(id) { UserId = userId, State = ContactState.Regular };
        }
        if (isBlocked)
            contact = contact with { State = ContactState.Blocked };

        var version = VersionGenerator.NextVersion(dbContact?.Version ?? 0);
        dbContext.Add(new DbArchivedContact(contact, Clocks.SystemClock.Now, version));
        if (dbContact != null)
            dbContext.Remove(dbContact);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        DeferArchiveInvalidation(ownerId, id);
        if (existing != null) {
            // The contact leaves the contact list, which is what the event consumers track
            var context = CommandContext.GetCurrent();
            context.Operation.AddEvent(new ContactChangedEvent(contact, existing, ChangeKind.Remove));
        }
    }

    private void DeferArchiveInvalidation(UserId ownerId, ContactId id)
        => Invalidation.Defer(() => InvalidateArchive(ownerId, id));

    private void InvalidateArchive(UserId ownerId, ContactId id)
    {
        var chatId = id.ChatId;
        var placeId = (chatId as PlaceChatId)?.PlaceId;
        _ = Get(ownerId, id, default);
        _ = ListIds(ownerId, placeId, default);
        _ = GetArchivedCount(ownerId, placeId, default);
        if (chatId is not PeerChatId peerChatId)
            return;

        _ = ListBlockedIds(ownerId, default);
        var otherUserId = peerChatId.AnotherUserIdOrNull(ownerId);
        if (otherUserId is null)
            return;

        _ = IsBlocked(ownerId, otherUserId, default);
        // Other side's IsBlockedByPeer depends on ownerId's IsBlocked
        _ = Get(otherUserId, ContactId.NewUser(otherUserId, ownerId), default);
    }

    private async Task EnsureThreadContactExits(UserId userId, ThreadChatId chatId, CancellationToken cancellationToken)
    {
        var contactId = ContactId.NewAny(userId, chatId);
        var threadContact = await GetThreadContact(userId, contactId, cancellationToken).ConfigureAwait(false);
        if (threadContact is not null)
            return;

        var command = new ContactsBackend_ChangeThreadContact(contactId, null, Change.Create(new ThreadContact(contactId)));
        await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);
    }
}
