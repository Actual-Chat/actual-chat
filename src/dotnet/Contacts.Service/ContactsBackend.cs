using ActualChat.Contacts.Db;
using ActualChat.Db;
using ActualChat.Users;
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
    private IExternalContactsBackend ExternalContactsBackend => field ??= Services.GetRequiredService<IExternalContactsBackend>();
    private IRolesBackend RolesBackend => field ??= Services.GetRequiredService<IRolesBackend>();
    private IDbEntityResolver<string, DbContact> DbContactResolver => field ??= Services.GetRequiredService<IDbEntityResolver<string, DbContact>>();
    private IMeshLocks GreetLocks => field ??= Services.MeshLocks().WithKeyPrefix(nameof(GreetLocks));
    public RedisDb<ContactsDbContext> RedisDb => field ??= Services.GetRequiredService<RedisDb<ContactsDbContext>>();
    private IDbEntityResolver<string, DbThreadContact> DbThreadContactResolver => field ??= Services.GetRequiredService<IDbEntityResolver<string, DbThreadContact>>();

    // [ComputeMethod]
    public virtual async Task<Contact> Get(UserId ownerId, ContactId contactId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ownerId);
        ArgumentNullException.ThrowIfNull(contactId.ChatId);
        ArgumentOutOfRangeException.ThrowIfNotEqual(ownerId, contactId.OwnerId);

        var dbContact = await DbContactResolver.Get(contactId.Value, cancellationToken).ConfigureAwait(false);
        var contact = dbContact?.ToModel()
            ?? new Contact(contactId); // A fake contact

        var chatId = contact.ChatId;
        if (chatId is PeerChatId peerChatId) {
            var userId = peerChatId.AnotherUserIdOrNull(ownerId);
            if (userId is null)
                throw new ArgumentOutOfRangeException(nameof(contactId));

            var account = await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
            contact = contact with { Account = account.ToAccount() };
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
            var publicChatIds = await ChatsBackend.GetPublicChatIdsFor(placeId, cancellationToken).ConfigureAwait(false);
            var addedChatIds = contactIds.Select(c => c.ChatId).ToHashSet();
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
    public async Task<Contact[]> ListChangedPlaceContacts(ChangedContactsQuery query, CancellationToken cancellationToken)
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

        if (Invalidation.IsActive) {
            var invIndex = context.Operation.Items.KeylessGet(long.MinValue);
            if (invIndex != long.MinValue) {
                _ = Get(ownerId, id, default);
                _ = ListIds(ownerId, placeId, default);
            }
            return default!;
        }

        id.Require();
        ownerId.Require();
        change.RequireValid();
        var oldContactIds = await ListIds(ownerId, placeId, cancellationToken).ConfigureAwait(false);

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        await dbContext.Contacts.Lock(id, cancellationToken).ConfigureAwait(false);
        var dbContact = await dbContext.Contacts
            .FirstOrDefaultAsync(c => c.Id == id.Value, cancellationToken)
            .ConfigureAwait(false);
        var existing = dbContact?.ToModel();

        if (change.IsCreate(out var contact)) {
            if (dbContact != null) {
                var existingContact = existing.Require();
                if (contact.State != ContactState.Regular || existingContact.State == ContactState.Regular)
                    return existingContact; // Already exists, so we don't recreate one

                contact = existingContact with {
                    Version = VersionGenerator.NextVersion(dbContact.Version),
                    State = ContactState.Regular,
                    TouchedAt = Clocks.SystemClock.Now,
                };
                dbContact.UpdateFrom(contact);
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                context.Operation.Items.KeylessSet((long)oldContactIds.IndexOf(id));
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
            dbContact.RequireVersion(expectedVersion);
            contact = contact with {
                Version = VersionGenerator.NextVersion(dbContact.Version),
            };
            dbContact.UpdateFrom(contact);
        }
        else { // Remove
            if (expectedVersion != null)
                dbContact.RequireVersion(expectedVersion);
            if (dbContact == null)
                return null;

            dbContext.Remove(dbContact);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation.Items.KeylessSet(change.Update.HasValue ? oldContactIds.IndexOf(id) : -1L);
        contact = dbContact.ToModel();
        context.Operation.AddEvent(new ContactChangedEvent(contact, existing, change.Kind));
        return contact;
    }

    // [CommandHandler]
    public virtual async Task OnTouch(ContactsBackend_Touch command, CancellationToken cancellationToken)
    {
        var id = command.Id;
        var ownerId = id.OwnerId;
        var chatId = id.ChatId;
        var placeId = (chatId as PlaceChatId)?.PlaceId;
        var context = CommandContext.GetCurrent();

        if (Invalidation.IsActive) {
            var invIndex = context.Operation.Items.KeylessGet(long.MinValue);
            if (invIndex != long.MinValue) {
                _ = Get(ownerId, id, default);
                // Contacts are sorted by TouchedAt, and we load contacts in 2 stages: the 1st is limited by MinLoadLimit,
                // hence we need to invalidate ListIds for Update only in case it was not in MinLoadList before the change.
                if (invIndex < 0 || invIndex > Constants.Contacts.MinLoadLimit)
                    _ = ListIds(ownerId, placeId, default); // Create, Delete or move into MinLoadLimit
            }
            return;
        }

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
        context.Operation.Items.KeylessSet((long)contactIds.IndexOf(id));
    }

    // [CommandHandler]
    public virtual async Task OnRemoveAccount(ContactsBackend_RemoveAccount command, CancellationToken cancellationToken)
    {
        var userId = command.UserId;
        if (Invalidation.IsActive)
            return; // spawns commands to remove contacts for other owners, we can skip invalidation for own contacts

        // var contactIds = await ListIds(userId, cancellationToken).ConfigureAwait(false);

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var idPrefix = userId.Value + ' ';
        await dbContext.Contacts
            .Where(a => a.Id.StartsWith(idPrefix)) // This is faster than index-based approach
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        var contactIds = await dbContext.Contacts
            .Where(a => a.UserId == userId.Value)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

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
        var context = CommandContext.GetCurrent();

        if (Invalidation.IsActive) {
            var invPlaceId = context.Operation.Items.KeylessGet<PlaceId>();
            if (invPlaceId is not null)
                _ = PseudoPlaceContact(invPlaceId);
            var invChatId = context.Operation.Items.KeylessGet<ChatId>();
            if (invChatId is not null)
                _ = PseudoChatContact(invChatId);
            return;
        }

        if (chatId is PlaceChatId { IsRoot: true } placeChatId) {
            var placeId = placeChatId.PlaceId;

            var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
            await using var __ = dbContext.ConfigureAwait(false);

            await dbContext.PlaceContacts
                .Where(c => c.PlaceId == placeId.Value)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            context.Operation.Items.KeylessSet(placeId);
        }
        else {
            var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
            await using var __ = dbContext.ConfigureAwait(false);

            await dbContext.Contacts
                .Where(c => c.ChatId == chatId.Value)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            context.Operation.Items.KeylessSet(chatId);
        }
    }

    // [CommandHandler]
    public virtual async Task OnGreet(ContactsBackend_Greet command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var account = await AccountsBackend.Get(command.UserId, cancellationToken).ConfigureAwait(false);
        if (account is null || account.IsGreetingCompleted)
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
            await Commander.Call(completeCmd, true, cancellationToken).ConfigureAwait(false);
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

        if (Invalidation.IsActive) {
            var invOwnerId = context.Operation.Items.KeylessGet<UserId>();
            if (invOwnerId is not null)
                _ = ListPlaceIds(invOwnerId, default);
            return;
        }

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
            context.Operation.Items.KeylessSet(ownerId);
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

        if (Invalidation.IsActive) {
            _ = PseudoChatContact(chatId);
            _ = PseudoPlaceContact(placeId);
            return;
        }

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

        Log.LogInformation("<- OnPublishCopiedChat: created contacts for chat '{ChatId}'", chatId);
    }

    // [CommandHandler]
    public virtual async Task OnReviewExternalContactName(
        ContactsBackend_ReviewExternalContactName command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

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

        if (Invalidation.IsActive) {
            _ = GetThreadContact(ownerId, id, default);
            var threadChatId = (ThreadChatId)id.ChatId;
            ThreadChatId? invChatId = threadChatId;
            while (invChatId is not null) {
                _ = ListThreadIdsForChat(ownerId, invChatId.ParentChatId, default);
                invChatId = invChatId.ParentChatId as ThreadChatId;
            }
            var parentChat = threadChatId.GetOutermostParent();
            var placeId = parentChat is PlaceChatId placeChatId ? placeChatId.PlaceId : null;
            _ = ListThreadIdsForPlace(ownerId, placeId, default);
            return default!;
        }

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
        return threadContact;
    }

    // Events

    // [EventHandler]
    public virtual async Task OnChatChangedEvent(ChatChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (chat, oldChat, changeKind) = eventCommand;
        if (chat.Id.IsThread(out var threadChatId)) {
            if (changeKind is ChangeKind.Remove) {
                // TODO: implement remove thread contacts
                // var command = new ContactsBackend_RemoveChatContacts(chat.Id);
                // await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);
            }
            else if (changeKind is ChangeKind.Create) {
                // Create a thread contact for the thread starter.
                var ownerRole = await RolesBackend
                    .GetSystem(chat.Id, SystemRole.Owner, cancellationToken)
                    .Require()
                    .ConfigureAwait(false);

                var authorIds = await RolesBackend.ListAuthorIds(chat.Id, ownerRole.Id, cancellationToken).ConfigureAwait(false);
                foreach (var authorId in authorIds) {
                    var author = await AuthorsBackend
                        .Get(authorId.ChatId, authorId, RequestedAuthorKind.Full, cancellationToken)
                        .ConfigureAwait(false);
                    if (author is null)
                        continue;

                    await EnsureThreadContactExits(author.UserId, threadChatId, cancellationToken).ConfigureAwait(false);
                }
            }
            return;
        }

        if (changeKind == ChangeKind.Remove) {
            var command = new ContactsBackend_RemoveChatContacts(chat.Id);
            await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);
        }
        else if (changeKind == ChangeKind.Update) {
            if (chat.IsArchived && oldChat?.IsArchived != true) {
                // Chat has been archived, remove contacts to it.
                var command = new ContactsBackend_RemoveChatContacts(chat.Id);
                await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    // [EventHandler]
    public virtual async Task OnAuthorChangedEvent(AuthorUpsertedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

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
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

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
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

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
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (threadChatId, mentionIds) = eventCommand;
        var parentChat = threadChatId.GetOutermostParent();
        foreach (var mentionId in mentionIds) {
            AuthorFull? author = null;
            if (mentionId.PrincipalId is AuthorId authorId)
                author = await AuthorsBackend
                    .Get(parentChat, authorId, RequestedAuthorKind.Full, cancellationToken)
                    .ConfigureAwait(false);
            else if (mentionId.PrincipalId is UserId userId)
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
