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

    private IAccountsBackend AccountsBackend => _accountsBackend ??= Services.GetRequiredService<IAccountsBackend>();
    private IChatsBackend ChatsBackend => _chatsBackend ??= Services.GetRequiredService<IChatsBackend>();
    private IDbEntityResolver<string, DbContact> DbContactResolver { get; }

    public ContactsBackend(IServiceProvider services) : base(services)
        => DbContactResolver = services.GetRequiredService<IDbEntityResolver<string, DbContact>>();

    // [ComputeMethod]
    public virtual async Task<Contact?> Get(UserId ownerId, ContactId contactId, CancellationToken cancellationToken)
    {
        if (contactId.OwnerId != (Symbol)ownerId)
            return null;

        var dbContact = await DbContactResolver.Get(contactId, cancellationToken).ConfigureAwait(false);
        var contact = dbContact?.ToModel()
            ?? new Contact(contactId); // A fake contact

        var chatId = contact.ChatId;
        if (chatId.Kind == ChatKind.Peer) {
            var userId = PeerChatId.ParseOrDefault(chatId).OtherThan(ownerId);
            if (userId.IsEmpty)
                return null;

            var account = await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
            if (account == null)
                return null;

            contact = contact with { Account = account.ToAccount() };
        }

        return contact;
    }

    // [ComputeMethod]
    public virtual async Task<Contact?> GetForChat(UserId ownerId, ChatId chatId, CancellationToken cancellationToken)
    {
        if (ownerId.IsEmpty || chatId.IsEmpty)
            return null;

        var contactId = new ContactId(ownerId, chatId, ParseOptions.Skip);
        return await Get(ownerId, contactId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<Contact?> GetForUser(UserId ownerId, UserId userId, CancellationToken cancellationToken)
    {
        if (ownerId.IsEmpty || userId.IsEmpty)
            return null;

        var peerChatId = PeerChatId.New(ownerId, userId);
        var id = new ContactId(ownerId, peerChatId, ParseOptions.Skip);
        return await Get(ownerId, id, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<ContactId>> ListIds(UserId ownerId, CancellationToken cancellationToken)
    {
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

    public async Task<Contact> GetOrCreateUserContact(UserId ownerId, UserId userId, CancellationToken cancellationToken)
    {
        var contact = await GetForUser(ownerId, userId, cancellationToken).ConfigureAwait(false);
        if (contact.IsStored())
            return contact;

        var peerChatId = PeerChatId.New(ownerId, userId);
        var contactId = new ContactId(ownerId, peerChatId, ParseOptions.Skip);
        var command = new IContactsBackend.ChangeCommand(contactId, null, new Change<Contact> {
            Create = new Contact(contactId),
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

        id.RequireNonEmpty();
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
            _ = Get(id.OwnerId, id, default);
            return;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

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

        var contactId = new ContactId(userId, chatId, ParseOptions.Skip);
        var contact = await Get(userId, contactId, cancellationToken).ConfigureAwait(false);
        var hasNoStoredContact = contact is not { IsVirtual: true };
        if (author.HasLeft == hasNoStoredContact)
            return; // No need to make any changes

        var change = author.HasLeft
            ? new Change<Contact>() { Remove = true }
            : new Change<Contact>() { Create = new Contact(contactId) };
        var command = new IContactsBackend.ChangeCommand(contactId, null, change);
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
