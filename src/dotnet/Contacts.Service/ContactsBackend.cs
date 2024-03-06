using ActualChat.Chat;
using ActualChat.Chat.Events;
using ActualChat.Commands;
using ActualChat.Contacts.Db;
using ActualChat.Contacts.Module;
using ActualChat.Db;
using ActualChat.Mesh;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Redis;

namespace ActualChat.Contacts;

public class ContactsBackend(IServiceProvider services) : DbServiceBase<ContactsDbContext>(services), IContactsBackend
{
    public static readonly TimeSpan GreetTimeout = TimeSpan.FromSeconds(20);

    private const string RedisKeyPrefix = ".ContactGreetingLocks.";
    private IAccountsBackend? _accountsBackend;
    private IChatsBackend? _chatsBackend;
    private IExternalContactsBackend? _externalContactsBackend;
    private RedisDb<ContactsDbContext>? _redisDb;

    private IAccountsBackend AccountsBackend => _accountsBackend ??= Services.GetRequiredService<IAccountsBackend>();
    private IChatsBackend ChatsBackend => _chatsBackend ??= Services.GetRequiredService<IChatsBackend>();
    private IExternalContactsBackend ExternalContactsBackend
        => _externalContactsBackend ??= Services.GetRequiredService<IExternalContactsBackend>();
    private IDbEntityResolver<string, DbContact> DbContactResolver { get; }
        = services.GetRequiredService<IDbEntityResolver<string, DbContact>>();
    private IMeshLocks GreetLocks { get; }
        = services.MeshLocks<ContactsDbContext>().WithKeyPrefix(nameof(GreetLocks));

    public RedisDb<ContactsDbContext> RedisDb => _redisDb ??= Services.GetRequiredService<RedisDb<ContactsDbContext>>();

    // [ComputeMethod]
    public virtual async Task<Contact> Get(UserId ownerId, ContactId contactId, CancellationToken cancellationToken)
    {
        if (ownerId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(ownerId));
        ArgumentOutOfRangeException.ThrowIfNotEqual(ownerId, contactId.OwnerId);

        var dbContact = await DbContactResolver.Get(contactId, cancellationToken).ConfigureAwait(false);
        var contact = dbContact?.ToModel()
            ?? new Contact(contactId); // A fake contact

        var chatId = contact.ChatId;
        if (chatId.IsPeerChat(out var peerChatId)) {
            var userId = peerChatId.UserIds.OtherThanOrDefault(ownerId);
            if (userId.IsGuestOrNone)
                throw new ArgumentOutOfRangeException(nameof(contactId));

            var account = await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
            contact = contact with { Account = account.ToAccount() };
        }

        // Subscribe on Chat removal
        if (!contactId.ChatId.IsNone && contactId.ChatId != Constants.Chat.AnnouncementsChatId)
            await PseudoChatContact(contactId.ChatId).ConfigureAwait(false);

        return contact;
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<ContactId>> ListIdsForEntrySearch(UserId userId, CancellationToken cancellationToken)
    {
        var nonPlaceContactIds = await ListIds(userId, PlaceId.None, cancellationToken).ConfigureAwait(false);
        var placeIds = await ListPlaceIds(userId, cancellationToken).ConfigureAwait(false);
        var placeContactIds = await placeIds.Select(placeId => ListIdsForSearch(userId, placeId, false, cancellationToken))
            .Collect()
            .Flatten()
            .ConfigureAwait(false);
        return nonPlaceContactIds.Concat(placeContactIds)
            .Concat(placeIds.Select(x => new ContactId(userId, x.ToRootChatId())))
            .ToApiArray();
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<ContactId>> ListIdsForContactSearch(UserId userId, CancellationToken cancellationToken)
    {
        var nonPlacePrivateChatContactIds = await ListIdsForSearch(userId, PlaceId.None, false, cancellationToken).ConfigureAwait(false);
        var placeIds = await ListPlaceIds(userId, cancellationToken).ConfigureAwait(false);
        var places = await GetPlaces().ConfigureAwait(false);
        // for private place we also include public chats
        var placeChatContactIds = await places.Select(x => ListIdsForSearch(userId, x.Id, !x.IsPublic, cancellationToken))
            .Collect()
            .Flatten()
            .ConfigureAwait(false);
        var privatePlaceRootChatContactIds = places.Where(x => !x.IsPublic).Select(x => new ContactId(userId, x.Id.ToRootChatId()));
        return nonPlacePrivateChatContactIds.Concat(placeChatContactIds)
            .Concat(privatePlaceRootChatContactIds)
            .ToApiArray();

        async Task<Place[]> GetPlaces()
        {
            var allPlaces = await placeIds.Select(x => ChatsBackend.GetPlace(x, cancellationToken)).Collect().ConfigureAwait(false);
            return allPlaces.SkipNullItems().ToArray();
        }
    }

    [ComputeMethod]
    protected virtual async Task<ApiArray<ContactId>> ListIdsForSearch(UserId userId, PlaceId placeId, bool includePublic, CancellationToken cancellationToken)
    {
        var contactIds = await ListIds(userId, placeId, cancellationToken).ConfigureAwait(false);
        if (includePublic)
            return contactIds;

        var publicChatIds = await ChatsBackend.GetPublicChatIdsFor(placeId, cancellationToken).ConfigureAwait(false);
        return contactIds.ExceptBy(publicChatIds, x => x.ChatId).ToApiArray();
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<ContactId>> ListIds(UserId ownerId, PlaceId placeId, CancellationToken cancellationToken)
    {
        if (ownerId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(ownerId));

        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var idPrefix = ownerId.Value + ' ';
        var sPlaceId = placeId.Id.Value.NullIfEmpty();
        var sContactIds = await dbContext.Contacts
            .Where(a => a.Id.StartsWith(idPrefix)) // This is faster than index-based approach
            .Where(a => a.PlaceId == sPlaceId)
            .OrderByDescending(a => a.TouchedAt)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        ApiArray<ContactId> result;
        if (placeId.IsNone) {
            var announcementChatContactId = new ContactId(ownerId, Constants.Chat.AnnouncementsChatId);
            if (!sContactIds.Any(c => OrdinalEquals(c, announcementChatContactId.Value)))
                sContactIds.Add(announcementChatContactId);
            result = sContactIds.ToApiArray(c => new ContactId(c));
        }
        else {
            await PseudoPlaceContact(placeId).ConfigureAwait(false);
            var chatIds = await ChatsBackend.GetPublicChatIdsFor(placeId, cancellationToken).ConfigureAwait(false);
            var contactIds = sContactIds.Select(c => new ContactId(c)).ToList();
            var addedChatIds = contactIds.Select(c => c.ChatId).ToList();
            var chatIdsToAdd = chatIds.Except(addedChatIds).ToList();
            if (chatIdsToAdd.Count > 0) {
                var contactsToAdd = chatIdsToAdd.Select(c => new ContactId(ownerId, c, AssumeValid.Option)).ToList();
                contactIds.InsertRange(0, contactsToAdd);
            }
            result = contactIds.ToApiArray();
        }

        // Subscribe on Chat removal
        foreach (var contactId in result) {
            if (contactId.ChatId.IsNone)
                continue;
            if (contactId.ChatId == Constants.Chat.AnnouncementsChatId)
                continue;
            await PseudoChatContact(contactId.ChatId).ConfigureAwait(false);
        }

        return result;
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<PlaceId>> ListPlaceIds(UserId ownerId, CancellationToken cancellationToken)
    {
        if (ownerId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(ownerId));

        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var idPrefix = ownerId.Value + ' ';
        var contactIds = await dbContext.PlaceContacts
            .Where(a => a.Id.StartsWith(idPrefix)) // This is faster than index-based approach
            .OrderBy(a => a.Id)
            .Select(a => a.PlaceId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var result = contactIds.ToApiArray(c => new PlaceId(c, AssumeValid.Option));
        // Subscribe on Place removal
        foreach (var placeId in result)
            await PseudoPlaceContact(placeId).ConfigureAwait(false);
        return result;
    }

    // [CommandHandler]
    public virtual async Task<Contact?> OnChange(
        ContactsBackend_Change command,
        CancellationToken cancellationToken)
    {
        var (id, expectedVersion, change) = command;
        var ownerId = id.OwnerId;
        var chatId = id.ChatId;
        var placeId = chatId.PlaceId;
        var context = CommandContext.GetCurrent();

        if (Computed.IsInvalidating()) {
            var invIndex = context.Operation().Items.GetOrDefault(long.MinValue);
            if (invIndex != long.MinValue) {
                _ = Get(ownerId, id, default);
                if (invIndex < 0 || invIndex > Constants.Contacts.MinLoadLimit)
                    _ = ListIds(ownerId, placeId, default); // Create, Delete or move into MinLoadLimit
            }
            return default!;
        }

        id.Require();
        ownerId.Require();
        change.RequireValid();
        var oldContactIds = await ListIds(ownerId, placeId, cancellationToken).ConfigureAwait(false);

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbContact = await dbContext.Contacts.ForUpdate()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (change.IsCreate(out var contact)) {
            if (dbContact != null)
                return dbContact.ToModel(); // Already exists, so we don't recreate one

            // Original UserId is ignored here - it's set based on Id
            var userId = id.ChatId.IsPeerChat(out var peerChatId)
                ? peerChatId.UserIds.OtherThan(ownerId)
                : UserId.None;

            // Checks
            if (ownerId.IsGuest && !userId.IsNone)
                throw StandardError.Constraint("You must sign-in to chat with another user.");
            if (userId.IsGuest)
                throw StandardError.Constraint("You can't chat with unauthenticated user.");

            contact = contact with {
                Id = id,
                Version = VersionGenerator.NextVersion(),
                UserId = userId,
                IsPinned = contact.IsPinned,
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
        context.Operation().Items.Set(change.Update.HasValue ? oldContactIds.IndexOf(id) : -1L);
        contact = dbContact.ToModel();
        return contact;
    }

    // [CommandHandler]
    public virtual async Task OnTouch(ContactsBackend_Touch command, CancellationToken cancellationToken)
    {
        var id = command.Id;
        var ownerId = id.OwnerId;
        var chatId = id.ChatId;
        var placeId = chatId.PlaceId;
        var context = CommandContext.GetCurrent();
        if (Computed.IsInvalidating()) {
            var invIndex = context.Operation().Items.GetOrDefault(long.MinValue);
            if (invIndex != long.MinValue) {
                _ = Get(ownerId, id, default);
                // Contacts are sorted by TouchedAt and we load contacts in 2 stages: the 1st is limited by MinLoadLimit,
                // hence we need to invalidate ListIds for Update only in case it was not in MinLoadList before the change.
                if (invIndex < 0 || invIndex > Constants.Contacts.MinLoadLimit)
                    _ = ListIds(ownerId, placeId, default); // Create, Delete or move into MinLoadLimit
            }
            return;
        }

        var contactIds = await ListIds(ownerId, placeId, cancellationToken).ConfigureAwait(false);

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbContact = await dbContext.Contacts.ForUpdate()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
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
        context.Operation().Items.Set((long)contactIds.IndexOf(id));
    }

    // [CommandHandler]
    public virtual async Task OnRemoveAccount(ContactsBackend_RemoveAccount command, CancellationToken cancellationToken)
    {
        var userId = command.UserId;
        if (Computed.IsInvalidating())
            return; // spawns commands to remove contacts for other owners, we can skip invalidation for own contacts

        // var contactIds = await ListIds(userId, cancellationToken).ConfigureAwait(false);

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var idPrefix = userId.Value + ' ';
        await dbContext.Contacts
            .Where(a => a.Id.StartsWith(idPrefix)) // This is faster than index-based approach
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        var contactIds = await dbContext.Contacts
            .Where(a => a.UserId == userId)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var contactId in contactIds) {
            var removeCommand = new ContactsBackend_Change(new ContactId(contactId), null, new Change<Contact> { Remove = true });
            await Commander.Call(removeCommand, cancellationToken).ConfigureAwait(false);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnRemoveChatContacts(ContactsBackend_RemoveChatContacts command, CancellationToken cancellationToken)
    {
        var chatId = command.ChatId;
        var context = CommandContext.GetCurrent();

        if (Computed.IsInvalidating()) {
            var invPlaceId = context.Operation().Items.GetId<PlaceId>();
            if (!invPlaceId.IsNone)
                _ = PseudoPlaceContact(invPlaceId);
            var invChatId = context.Operation().Items.GetId<ChatId>();
            if (!invChatId.IsNone)
                _ = PseudoChatContact(invChatId);
            return;
        }

        if (chatId.IsPlaceChat && chatId.PlaceChatId.IsRoot) {
            var placeId = chatId.PlaceId;

            var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
            await using var __ = dbContext.ConfigureAwait(false);

            await dbContext.PlaceContacts
                .Where(c => c.PlaceId == placeId)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            context.Operation().Items.SetId(placeId);
        }
        else {
            var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
            await using var __ = dbContext.ConfigureAwait(false);

            await dbContext.Contacts
                .Where(c => c.ChatId == chatId)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            context.Operation().Items.SetId(chatId);
        }
    }

    // [CommandHandler]
    public virtual async Task OnGreet(ContactsBackend_Greet command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var account = await AccountsBackend.Get(command.UserId, cancellationToken).ConfigureAwait(false);
        if (account is null || account.IsGreetingCompleted)
            return;

        var h = await GreetLocks.TryLock(account.Id.Value, cancellationToken).ConfigureAwait(false);
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
            var referencingUserIds = await ExternalContactsBackend.ListReferencingUserIds(account.Id, cancellationToken)
                .ConfigureAwait(false);
            await referencingUserIds
                .Where(userId => userId != account.Id)
                .Select(CreateContact)
                .Collect()
                .ConfigureAwait(false);

            var completeCmd = new AccountsBackend_Update(account with { IsGreetingCompleted = true }, account.Version);
            await Commander.Call(completeCmd, true, cancellationToken).ConfigureAwait(false);
        }
        finally {
            await database.KeyDeleteAsync(alreadyGreetingKey).ConfigureAwait(false);
        }
        return;

        Task<Contact?> CreateContact(UserId ownerId) {
            var contact = new Contact(ContactId.Peer(ownerId, account.Id));
            var cmd = new ContactsBackend_Change(contact.Id, null, Change.Create(contact));
            return Commander.Call(cmd, true, cancellationToken);
        }
    }

    // [CommandHandler]
    public virtual async Task OnChangePlaceMembership(
        ContactsBackend_ChangePlaceMembership command,
        CancellationToken cancellationToken)
    {
        var (ownerId, placeId, hasLeft) = command;
        var context = CommandContext.GetCurrent();

        if (Computed.IsInvalidating()) {
            var invOwnerId = context.Operation().Items.GetId<UserId>();
            if (!invOwnerId.IsNone)
                _ = ListPlaceIds(invOwnerId, default);
            return;
        }

        ownerId.Require();
        placeId.Require();

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var id = DbPlaceContact.FormatId(ownerId, placeId);

        var dbPlaceContact = await dbContext.PlaceContacts.ForUpdate()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (dbPlaceContact != null) {
            if (hasLeft)
                dbContext.Remove(dbPlaceContact);
        }
        else {
            if (!hasLeft) {
                var newDbPlaceContact = new DbPlaceContact(ownerId, placeId) {
                    Version = VersionGenerator.NextVersion(),
                };
                dbContext.Add(newDbPlaceContact);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation().Items.SetId(ownerId);
    }

    // Events

    [EventHandler]
    public virtual async Task OnChatChangedEvent(ChatChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var (chat, oldChat, changeKind) = eventCommand;

        if (changeKind == ChangeKind.Remove) {
            var command = new ContactsBackend_RemoveChatContacts(chat.Id);
            await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);
        }
    }

    [EventHandler]
    public virtual async Task OnAuthorChangedEvent(AuthorChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var (author, oldAuthor) = eventCommand;
        var oldHasLeft = oldAuthor?.HasLeft ?? true;
        if (oldHasLeft == author.HasLeft && (oldAuthor?.Version ?? 0) != 0)
            return;

        var chatId = author.ChatId;
        var userId = author.UserId;
        if (chatId.IsNone || userId.IsNone) // Weird case
            return;
        if (chatId.Kind == ChatKind.Peer && author.HasLeft) // Users can't leave peer chats
            return;

        if (chatId.IsPlaceChat) {
            var changePlaceMembership = new ContactsBackend_ChangePlaceMembership(userId, chatId.PlaceId, author.HasLeft);
            await Commander.Call(changePlaceMembership, true, cancellationToken).ConfigureAwait(false);
            if (chatId.PlaceChatId.IsRoot)
                return;
        }

        var contactId = new ContactId(userId, chatId, AssumeValid.Option);
        var contact = await Get(userId, contactId, cancellationToken).ConfigureAwait(false);
        if (contact.IsStored() == !author.HasLeft)
            return; // No need to make any changes

        var change = author.HasLeft
            ? new Change<Contact> { Remove = true }
            : new Change<Contact> { Create = new Contact(contactId) };
        var command = new ContactsBackend_Change(contactId, null, change);
        await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);
    }

    [EventHandler]
    public virtual async Task OnTextEntryChangedEvent(TextEntryChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var (_, author, changeKind) = eventCommand;
        if (changeKind == ChangeKind.Remove)
            return;

        var userId = author.UserId;
        var chatId = author.ChatId;
        if (userId.IsNone) // We do nothing for anonymous authors for now
            return;

        var contactId = new ContactId(userId, chatId, ParseOrNone.Option);
        if (contactId.IsNone)
            return;

        var contact = await Get(userId, contactId, cancellationToken).ConfigureAwait(false);
        var now = Clocks.SystemClock.Now;
        if (now - contact.TouchedAt < Constants.Contacts.MinTouchInterval)
            return;

        var command = new ContactsBackend_Touch(contact.Id);
        await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task OnMoveChatToPlace(ContactsBackend_MoveChatToPlace command, CancellationToken cancellationToken)
    {
        var (chatId, placeId) = command;
        var placeChatId = new PlaceChatId(PlaceChatId.Format(placeId, chatId.Id));
        var newChatId = (ChatId)placeChatId;

        if (Computed.IsInvalidating()) {
            _ = PseudoChatContact(chatId);
            _ = PseudoChatContact(newChatId);
            _ = PseudoPlaceContact(placeId);
            return;
        }

        var chatSid = chatId.Value;
        var placeSid = placeId.Value;

        Log.LogInformation("OnMoveChatToPlace: starting, moving chat '{ChatId}' to place '{PlaceId}'", chatSid, placeSid);
        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbContacts = await dbContext.Contacts
            .Where(c => c.ChatId == chatSid)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Update DbContacts
        foreach (var dbContact in dbContacts) {
            var contactSid = dbContact.Id;
            var ownerId = new UserId(dbContact.OwnerId);
            var newContactSid = ContactId.Format(ownerId, newChatId);
            var newChatSid = newChatId.Value;

            _ = await dbContext.Contacts
                .Where(c => c.Id == contactSid)
                .ExecuteUpdateAsync(setters => setters
                        .SetProperty(c => c.Id, _ => newContactSid)
                        .SetProperty(c => c.ChatId, _ => newChatSid)
                        .SetProperty(c => c.PlaceId, _ => placeSid),
                    cancellationToken)
                .RequireOneUpdated()
                .ConfigureAwait(false);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        // Place contacts should be added on adding authors to place root chat during processing ChatsMigrationBackend_MoveToPlace.
        Log.LogInformation("OnMoveChatToPlace: completed");
    }

    // Protected methods

    [ComputeMethod]
    protected virtual Task<Unit> PseudoPlaceContact(PlaceId placeId)
        => ActualLab.Async.TaskExt.UnitTask;

    [ComputeMethod]
    protected virtual Task<Unit> PseudoChatContact(ChatId chatId)
        => ActualLab.Async.TaskExt.UnitTask;

    // Private methods

    private static string ToRedisKey(UserId userId)
        => $"{RedisKeyPrefix}{userId.Value}";
}
