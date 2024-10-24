using ActualChat.Chat;
using ActualChat.Contacts.Db;
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
    private IAuthorsBackend? _authorsBackend;
    private IChatsBackend? _chatsBackend;
    private IExternalContactsBackend? _externalContactsBackend;
    private RedisDb<ContactsDbContext>? _redisDb;

    private IAccountsBackend AccountsBackend => _accountsBackend ??= Services.GetRequiredService<IAccountsBackend>();
    private IAuthorsBackend AuthorsBackend => _authorsBackend ??= Services.GetRequiredService<IAuthorsBackend>();
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
            var userId = peerChatId.AnotherUserIdOrDefault(ownerId);
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
    public virtual async Task<ApiArray<ContactId>> ListIdsForSearch(UserId userId, PlaceId? placeId, bool includePublic, CancellationToken cancellationToken)
    {
        if (placeId != null)
            return await ListPlaceContactIds(userId, placeId.Value, includePublic, cancellationToken).ConfigureAwait(false);

        var placeIds = await ListPlaceIds(userId, cancellationToken).ConfigureAwait(false);
        var contactIds = await placeIds.PrefixWith(PlaceId.None)
            .Select(id => ListPlaceContactIds(userId, id, includePublic, cancellationToken))
            .Collect(cancellationToken)
            .Flatten()
            .ConfigureAwait(false);
        return contactIds
            .Where(x => !x.ChatId.IsPlaceRootChat)
            .ToApiArray();
    }

    [ComputeMethod]
    protected virtual async Task<ApiArray<ContactId>> ListPlaceContactIds(UserId userId, PlaceId placeId, bool includePublic, CancellationToken cancellationToken)
    {
        var contactIds = await ListIds(userId, placeId, cancellationToken).ConfigureAwait(false);
        if (includePublic)
            return contactIds;

        var publicChatIds = await ChatsBackend.GetPublicChatIdsFor(placeId, cancellationToken).ConfigureAwait(false);
        return contactIds.ExceptBy(publicChatIds, x => x.ChatId).ToApiArray();
    }


    // [ComputeMethod]
    public virtual async Task<ApiArray<ContactId>> ListIdsForGroupContactSearch(UserId userId, PlaceId? placeId, CancellationToken cancellationToken)
    {
        var contactIds = await ListIdsForSearch(userId, placeId, true, cancellationToken).ConfigureAwait(false);
        return contactIds.Where(x => x.ChatId is { Kind: ChatKind.Group or ChatKind.Place })
            .ToApiArray();
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<ContactId>> ListPeerContactIds(
        UserId userId,
        PlaceId placeId,
        CancellationToken cancellationToken)
    {
        var contactIds = await ListIds(userId, PlaceId.None, cancellationToken).ConfigureAwait(false);
        var peerContactIds = contactIds.Where(x => x.ChatId.Kind == ChatKind.Peer).ToApiArray();
        if (placeId.IsNone)
            return peerContactIds;

        var placeUserIds = await AuthorsBackend.ListPlaceUserIds(placeId, cancellationToken).ConfigureAwait(false);
        return peerContactIds.IntersectBy(placeUserIds, x => x.ChatId.PeerChatId.UserIds.OtherThan(userId))
            .ToApiArray();
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<ContactId>> ListIds(UserId ownerId, PlaceId placeId, CancellationToken cancellationToken)
    {
        if (ownerId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(ownerId));

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
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
            var publicChatIds = await ChatsBackend.GetPublicChatIdsFor(placeId, cancellationToken).ConfigureAwait(false);
            var contactIds = sContactIds.Select(c => new ContactId(c)).ToList();
            var addedChatIds = contactIds.Select(c => c.ChatId).ToList();
            var chatIdsToAdd = publicChatIds.Except(addedChatIds).ToList();
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

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
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

        if (Invalidation.IsActive) {
            var invIndex = context.Operation.Items.GetOrDefault(long.MinValue);
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
        context.Operation.Items.Set(change.Update.HasValue ? oldContactIds.IndexOf(id) : -1L);
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

        if (Invalidation.IsActive) {
            var invIndex = context.Operation.Items.GetOrDefault(long.MinValue);
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

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
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
        context.Operation.Items.Set((long)contactIds.IndexOf(id));
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

        if (Invalidation.IsActive) {
            var invPlaceId = context.Operation.Items.GetOrDefault<PlaceId>();
            if (!invPlaceId.IsNone)
                _ = PseudoPlaceContact(invPlaceId);
            var invChatId = context.Operation.Items.GetOrDefault<ChatId>();
            if (!invChatId.IsNone)
                _ = PseudoChatContact(invChatId);
            return;
        }

        if (chatId.IsPlaceChat && chatId.PlaceChatId.IsRoot) {
            var placeId = chatId.PlaceId;

            var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
            await using var __ = dbContext.ConfigureAwait(false);

            await dbContext.PlaceContacts
                .Where(c => c.PlaceId == placeId)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            context.Operation.Items.Set(placeId);
        }
        else {
            var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
            await using var __ = dbContext.ConfigureAwait(false);

            await dbContext.Contacts
                .Where(c => c.ChatId == chatId)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            context.Operation.Items.Set(chatId);
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
                .Collect(cancellationToken)
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
        var (placeId, ownerId, hasLeft) = command;
        var context = CommandContext.GetCurrent();

        if (Invalidation.IsActive) {
            var invOwnerId = context.Operation.Items.GetOrDefault<UserId>();
            if (!invOwnerId.IsNone)
                _ = ListPlaceIds(invOwnerId, default);
            return;
        }

        ownerId.Require();
        placeId.Require();

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
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
        context.Operation.Items.Set(ownerId);
    }

    // [CommandHandler]
    public virtual async Task OnPublishCopiedChat(
        ContactsBackend_PublishCopiedChat command,
        CancellationToken cancellationToken)
    {
        var newChatId = command.NewChatId;

        if (Invalidation.IsActive) {
            _ = PseudoChatContact(newChatId);
            _ = PseudoPlaceContact(newChatId.PlaceId);
            return;
        }

        if (!newChatId.IsPlaceChat)
            throw StandardError.Constraint($"Place chat id is expected, but '{newChatId.Value}' is given.");

        var placeId = newChatId.PlaceId;

        Log.LogInformation("-> OnPublishCopiedChat: creating contacts for chat '{ChatId}'", newChatId);

        var authorIds = await AuthorsBackend.ListAuthorIds(newChatId, cancellationToken).ConfigureAwait(false);
        foreach (var authorId in authorIds) {
            var author = await AuthorsBackend.Get(authorId.ChatId, authorId, AuthorsBackend_GetAuthorOption.Full, cancellationToken).ConfigureAwait(false);
            if (author == null || author.HasLeft)
                continue;

            var userId = author.UserId;
            var changePlaceMembership = new ContactsBackend_ChangePlaceMembership(placeId, userId, false);
            await Commander.Call(changePlaceMembership, false, cancellationToken).ConfigureAwait(false);

            var contactId = new ContactId(userId, newChatId, AssumeValid.Option);
            var contact = await Get(userId, contactId, cancellationToken).ConfigureAwait(false);
            if (contact.IsStored())
                continue; // No need to make any changes

            var change = Change.Create(new Contact(contactId));
            var createContact = new ContactsBackend_Change(contactId, null, change);
            await Commander.Call(createContact, false, cancellationToken).ConfigureAwait(false);
        }

        Log.LogInformation("<- OnPublishCopiedChat: created contacts for chat '{ChatId}'", newChatId);
    }

    // Events

    // [EventHandler]
    public virtual async Task OnChatChangedEvent(ChatChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (chat, oldChat, changeKind) = eventCommand;

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
    public virtual async Task OnAuthorChangedEvent(AuthorChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
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
            var changePlaceMembership = new ContactsBackend_ChangePlaceMembership(chatId.PlaceId, userId, author.HasLeft);
            await Commander.Call(changePlaceMembership, true, cancellationToken).ConfigureAwait(false);
            if (chatId.PlaceChatId.IsRoot)
                return;
        }

        var contactId = new ContactId(userId, chatId, AssumeValid.Option);
        var contact = await Get(userId, contactId, cancellationToken).ConfigureAwait(false);
        if (contact.IsStored() == !author.HasLeft)
            return; // No need to make any changes

        if (chatId.Kind == ChatKind.Group && !author.HasLeft) {
            var chat = await ChatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
            if (chat is null)
                Log.LogWarning("Can't get chat with id '{ChatId}' on changing author '{Author}', old author: '{OldAuthor}'",
                    chatId, author, oldAuthor);
            else if (chat.SystemTag == Constants.Chat.SystemTags.Bot)
                return; // Do not create contacts for ML Search chats
        }

        var change = author.HasLeft
            ? new Change<Contact> { Remove = true }
            : new Change<Contact> { Create = new Contact(contactId) };
        var command = new ContactsBackend_Change(contactId, null, change);
        await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);
    }

    // [EventHandler]
    public virtual async Task OnTextEntryChangedEvent(TextEntryChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (_, author, changeKind, _) = eventCommand;
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
