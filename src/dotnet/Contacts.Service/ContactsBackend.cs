using ActualChat.Chat.Events;
using ActualChat.Commands;
using ActualChat.Contacts.Db;
using ActualChat.Contacts.Module;
using ActualChat.Users;
using ActualChat.Users.Events;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using Stl.Fusion.EntityFramework;
using Stl.Redis;

namespace ActualChat.Contacts;

public class ContactsBackend(IServiceProvider services) : DbServiceBase<ContactsDbContext>(services), IContactsBackend
{
    private const string RedisKeyPrefix = ".ContactGreetingLocks.";
    private ContactsSettings? _settings;
    private IAccountsBackend? _accountsBackend;
    private IExternalContactsBackend? _externalContactsBackend;
    private IDbEntityResolver<string, DbContact>? _dbContactResolver;
    private RedisDb<ContactsDbContext>? _redisDb;

    private ContactsSettings Settings => _settings ??= Services.GetRequiredService<ContactsSettings>();
    private IAccountsBackend AccountsBackend => _accountsBackend ??= Services.GetRequiredService<IAccountsBackend>();

    private IExternalContactsBackend ExternalContactsBackend => _externalContactsBackend ??= Services.GetRequiredService<IExternalContactsBackend>();

    private IDbEntityResolver<string, DbContact> DbContactResolver => _dbContactResolver ??= Services.GetRequiredService<IDbEntityResolver<string, DbContact>>();

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

        return contact;
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<ContactId>> ListIds(UserId ownerId, CancellationToken cancellationToken)
    {
        if (ownerId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(ownerId));

        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var idPrefix = ownerId.Value + ' ';
        var contactIds = await dbContext.Contacts
            .Where(a => a.Id.StartsWith(idPrefix)) // This is faster than index-based approach
            .OrderByDescending(a => a.TouchedAt)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var announcementChatContactId = new ContactId(ownerId, Constants.Chat.AnnouncementsChatId);
        if (!contactIds.Any(c => OrdinalEquals(c, announcementChatContactId.Value)))
            contactIds.Add(announcementChatContactId);

        // That's just a bit more efficient conversion than .Select().ToApiArray()
        var result = new ContactId[contactIds.Count];
        for (var i = 0; i < contactIds.Count; i++)
            result[i] = new ContactId(contactIds[i]);

        return new ApiArray<ContactId>(result);
    }

    // [CommandHandler]
    public virtual async Task<Contact?> OnChange(
        ContactsBackend_Change command,
        CancellationToken cancellationToken)
    {
        var (id, expectedVersion, change) = command;
        var ownerId = id.OwnerId;
        var context = CommandContext.GetCurrent();

        if (Computed.IsInvalidating()) {
            var invIndex = context.Operation().Items.GetOrDefault(long.MinValue);
            if (invIndex != long.MinValue) {
                _ = Get(ownerId, id, default);
                if (invIndex < 0 || invIndex > Constants.Contacts.MinLoadLimit)
                    _ = ListIds(ownerId, default); // Create, Delete or move into MinLoadLimit
            }
            return default!;
        }

        id.Require();
        ownerId.Require();
        change.RequireValid();
        var oldContactIds = await ListIds(ownerId, cancellationToken).ConfigureAwait(false);

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
        var context = CommandContext.GetCurrent();
        if (Computed.IsInvalidating()) {
            var invIndex = context.Operation().Items.GetOrDefault(long.MinValue);
            if (invIndex != long.MinValue) {
                _ = Get(ownerId, id, default);
                // Contacts are sorted by TouchedAt and we load contacts in 2 stages: the 1st is limited by MinLoadLimit,
                // hence we need to invalidate ListIds for Update only in case it was not in MinLoadList before the change.
                if (invIndex < 0 || invIndex > Constants.Contacts.MinLoadLimit)
                    _ = ListIds(ownerId, default); // Create, Delete or move into MinLoadLimit
            }
            return;
        }

        var contactIds = await ListIds(ownerId, cancellationToken).ConfigureAwait(false);

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
    public virtual async Task OnGreet(ContactsBackend_Greet command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var userToGreetId = command.UserId;
        var account = await AccountsBackend.Get(userToGreetId, cancellationToken).ConfigureAwait(false);
        if (account is null || account.IsGreetingCompleted)
            return;

        var alreadyGreetingKey = ToRedisKey(account.Id);
        var canStart = await RedisDb.Database.StringSetAsync(alreadyGreetingKey, Clocks.SystemClock.Now.ToString(), Settings.GreetingTimeout, When.NotExists).ConfigureAwait(false);
        if (!canStart)
            return;

        try {
            var referencingUserIds = await ExternalContactsBackend.ListReferencingUserIds(userToGreetId, cancellationToken)
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
            await RedisDb.Database.KeyDeleteAsync(alreadyGreetingKey).ConfigureAwait(false);
        }
        return;

        Task<Contact?> CreateContact(UserId ownerId) {
            var contact = new Contact(ContactId.Peer(ownerId, userToGreetId));
            var cmd = new ContactsBackend_Change(contact.Id, null, Change.Create(contact));
            return Commander.Call(cmd, true, cancellationToken);
        }
    }

    // Events

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

    // private methods

    private static string ToRedisKey(UserId userId)
        => $"{RedisKeyPrefix}{userId.Value}";
}
