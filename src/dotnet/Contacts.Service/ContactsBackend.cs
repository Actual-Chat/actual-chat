using ActualChat.Chat;
using ActualChat.Chat.Events;
using ActualChat.Commands;
using ActualChat.Contacts.Db;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Contacts;

public class ContactsBackend : DbServiceBase<ContactsDbContext>, IContactsBackend
{
    private IAccountsBackend? _accountsBackend;
    private IChatsBackend? _chatsBackend;
    private IAuthorsBackend? _authorsBackend;

    private IAccountsBackend AccountsBackend => _accountsBackend ??= Services.GetRequiredService<IAccountsBackend>();
    private IChatsBackend ChatsBackend => _chatsBackend ??= Services.GetRequiredService<IChatsBackend>();
    private IAuthorsBackend AuthorsBackend => _authorsBackend ??= Services.GetRequiredService<IAuthorsBackend>();
    private IDbEntityResolver<string, DbContact> DbContactResolver { get; }

    public ContactsBackend(IServiceProvider services) : base(services)
        => DbContactResolver = services.GetRequiredService<IDbEntityResolver<string, DbContact>>();

    // [ComputeMethod]
    public virtual async Task<Contact?> Get(string ownerId, string id, CancellationToken cancellationToken)
    {
        var dbContact = await DbContactResolver.Get(id, cancellationToken).ConfigureAwait(false);
        var contact = dbContact?.ToModel();
        if (contact is not { Id.IsValid: true })
            return null;
        if (contact.Id.OwnerId != ownerId)
            return null;

        var chatId = contact.Id.GetChatId();
        var chatTask = ChatsBackend.Get(chatId, cancellationToken);
        if (contact.Id.IsUserContact(out var userId)) {
            var account = await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
            if (account == null)
                return null;

            var chat = await chatTask.ConfigureAwait(false);
            chat ??= new Chat.Chat() {
                Id = chatId,
                Kind = ChatKind.Peer,
            };
            chat = chat with {
                Title = account.Avatar.Name,
                Picture = account.Avatar.Picture,
            };
            contact = contact with {
                Account = account,
                Chat = chat,
                ChatId = chatId,
            };
        }
        else if (contact.Id.IsChatContact(out _)) {
            var chat = await chatTask.ConfigureAwait(false);
            if (chat == null)
                return null;

            contact = contact with {
                Chat = chat,
                ChatId = chatId,
            };
        }
        else
            return null;

        return contact;
    }

    // [ComputeMethod]
    public virtual async Task<Contact?> GetForChat(string ownerId, string chatId, CancellationToken cancellationToken)
    {
        if (ownerId.IsNullOrEmpty() || chatId.IsNullOrEmpty())
            return null;

        var parsedChatId = new ChatId(chatId);
        ContactId id;
        switch (parsedChatId.Kind) {
        case ChatKind.Group:
            id = new ContactId(ownerId, parsedChatId.Id, ContactKind.Chat);
            break;
        case ChatKind.PeerFull:
            id = new ContactId(ownerId, parsedChatId.GetPeerChatTargetUserId(ownerId), ContactKind.User);
            break;
        default:
            return null;
        }

        return await Get(ownerId, id, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<Contact?> GetForUser(string ownerId, string userId, CancellationToken cancellationToken)
    {
        if (ownerId.IsNullOrEmpty() || userId.IsNullOrEmpty())
            return null;

        var id = new ContactId(ownerId, userId, ContactKind.User);
        return await Get(ownerId, id, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<ContactId>> ListIds(string ownerId, CancellationToken cancellationToken)
    {
        var parsedOwnerId = (UserId) ownerId;
        if (!parsedOwnerId.IsValid) // We need to make sure it's valid before using it in idPrefix
            return ImmutableArray<ContactId>.Empty;

        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var idPrefix = ownerId + ' ';
        var contactIds = await dbContext.Contacts
            .Where(a => a.Id.StartsWith(idPrefix)) // This is faster than index-based approach
            .OrderBy(a => a.Id)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // That's just a bit more efficient conversion than .Select().ToImmutableArray()
        var result = new ContactId[contactIds.Count];
        for (var i = 0; i < contactIds.Count; i++)
            result[i] = new ContactId(contactIds[i]);
        return ImmutableArray.Create(result);
    }

    public async Task<Contact> GetOrCreateUserContact(string ownerId, string userId, CancellationToken cancellationToken)
    {
        var contact = await GetForUser(ownerId, userId, cancellationToken).ConfigureAwait(false);
        if (contact != null)
            return contact;

        var id = new ContactId(ownerId, userId, ContactKind.User).RequireFullyValid();
        var command = new IContactsBackend.ChangeCommand(id, null, new Change<Contact> {
            Create = new Contact { Id = id },
        });

        contact = await Commander.Call(command, false, cancellationToken).ConfigureAwait(false);
        return contact;
    }

    // [CommandHandler]
    public virtual async Task<Contact> Change(
        IContactsBackend.ChangeCommand command,
        CancellationToken cancellationToken)
    {
        var (id, expectedVersion, change) = command;
        var context = CommandContext.GetCurrent();

        if (Computed.IsInvalidating()) {
            var invContact = context.Operation().Items.Get<Contact>();
            if (invContact != null) {
                _ = Get(invContact.Id.OwnerId, invContact.Id, default);
                if (!change.Update.HasValue) // Create or Delete
                    _ = ListIds(invContact.Id.OwnerId, default);
            }
            return default!;
        }

        id.RequireFullyValid();
        change.RequireValid();
        var dbId = id.Value;

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        DbContact? dbContact;
        if (change.IsCreate(out var contact)) {
            dbContact = await dbContext.Contacts.ForUpdate()
                .SingleOrDefaultAsync(c => c.Id == dbId, cancellationToken)
                .ConfigureAwait(false);
            if (dbContact != null)
                return dbContact.ToModel(); // Already exist, so we don't recreate one

            contact = contact with {
                Id = id,
                Version = VersionGenerator.NextVersion(),
                TouchedAt = Clocks.SystemClock.Now,
            };
            dbContact = new DbContact(contact);
            dbContext.Add(dbContact);
        }
        else {
            // Update or Delete
            dbContact = await dbContext.Contacts.ForUpdate()
                .SingleOrDefaultAsync(c => c.Id == dbId, cancellationToken)
                .ConfigureAwait(false);
            dbContact = dbContact.RequireVersion(expectedVersion);
            if (change.IsUpdate(out contact)) {
                contact = contact with {
                    Version = VersionGenerator.NextVersion(dbContact.Version),
                };
                dbContact.UpdateFrom(contact);
            }
            else
                dbContext.Remove(dbContact);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        contact = dbContact.ToModel();
        context.Operation().Items.Set(contact);
        return contact;
    }

    // [CommandHandler]
    public virtual async Task Touch(IContactsBackend.TouchCommand command, CancellationToken cancellationToken)
    {
        var id = command.Id;
        if (Computed.IsInvalidating()) {
            if (id.IsFullyValid)
                _ = Get(id.OwnerId, id, default);
            return;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        id.RequireFullyValid();
        // Update or Delete
        var dbId = id.Value;
        var dbContact = await dbContext.Contacts.ForUpdate()
            .SingleOrDefaultAsync(c => c.Id == dbId, cancellationToken)
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
    }

    // Events

    [EventHandler]
    public virtual async Task OnAuthorChangedEvent(AuthorChangedEvent @event, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var (author, _) = @event;
        var userId = author.UserId;
        var chatId = author.ChatId;
        if (userId.IsEmpty) // We do nothing for anonymous authors for now
            return;

        var parsedChatId = new ChatId(chatId).RequireValid();
        ContactId id;
        switch (parsedChatId.Kind) {
        case ChatIdKind.Group:
            id = new ContactId(userId, chatId, ContactKind.Chat);
            break;
        case ChatIdKind.PeerFull:
            id = new ContactId(userId, parsedChatId.GetPeerChatTargetUserId(userId), ContactKind.User);
            break;
        default:
            Log.LogWarning("Invalid event (unsupported ChatId kind): {Event}", @event);
            return;
        }

        var contact = await Get(userId, id, cancellationToken).ConfigureAwait(false);
        if ((contact == null) == author.HasLeft)
            return; // No need to make any changes

        var change = author.HasLeft
            ? new Change<Contact>() { Remove = true }
            : new Change<Contact>() { Create = new Contact(id) };
        var command = new IContactsBackend.ChangeCommand(id, null, change);
        await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);
    }

    [EventHandler]
    public virtual async Task OnTextEntryChangedEvent(TextEntryChangedEvent @event, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var (_, author, changeKind) = @event;
        if (changeKind != ChangeKind.Create)
            return;

        var userId = author.UserId;
        var chatId = author.ChatId;
        if (userId.IsEmpty) // We do nothing for anonymous authors for now
            return;

        var contact = await GetForChat(userId, chatId, cancellationToken).ConfigureAwait(false);
        if (contact == null)
            return;

        var command = new IContactsBackend.TouchCommand(contact.Id);
        await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);
    }
}
