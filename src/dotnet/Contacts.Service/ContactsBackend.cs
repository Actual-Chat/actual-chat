using ActualChat.Chat;
using ActualChat.Commands;
using ActualChat.Contacts.Db;
using ActualChat.Users;
using Cysharp.Text;
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
    public virtual async Task<Contact?> Get(string ownerId, string id, CancellationToken cancellationToken)
    {
        var dbContact = await DbContactResolver.Get(id, cancellationToken).ConfigureAwait(false);
        var contact = dbContact?.ToModel();
        if (contact is not { Id.IsValid: true })
            return null;
        if (contact.Id.OwnerId != ownerId)
            return null;

        if (contact.Id.IsUserContact(out var userId)) {
            var account = await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
            contact = contact with { Account = account };
        }
        else if (contact.Id.IsChatContact(out var chatId)) {
            var chat = await ChatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
            contact = contact with { Chat = chat };
        }
        else
            return null;

        return contact;
    }

    // [ComputeMethod]
    public virtual async Task<Contact?> GetUserContact(string ownerId, string userId, CancellationToken cancellationToken)
    {
        if (ownerId.IsNullOrEmpty() || userId.IsNullOrEmpty())
            return null;

        var id = new ContactId(ownerId, userId, ContactKind.User);
        return await Get(ownerId, id, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<ContactId>> List(string ownerId, CancellationToken cancellationToken)
    {
        var parsedOwnerId = (ParsedUserId) ownerId;
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
        var contact = await GetUserContact(ownerId, userId, cancellationToken).ConfigureAwait(false);
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
        var context = CommandContext.GetCurrent();
        var (id, expectedVersion, change) = command;
        if (Computed.IsInvalidating()) {
            var invContact = context.Operation().Items.Get<Contact>();
            if (invContact != null) {
                _ = Get(invContact.Id.OwnerId, invContact.Id, default);
                if (!change.Update.HasValue) // Create or Delete
                    _ = List(invContact.Id.OwnerId, default);
            }
            return default!;
        }

        id.RequireFullyValid();
        change.RequireValid();
        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        DbContact? dbContact;
        if (change.IsCreate(out var contact)) {
            dbContact = await dbContext.Contacts.Get(id.Format(), cancellationToken).ConfigureAwait(false);
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
            dbContact = await dbContext.Contacts
                .Get(id.Format(), cancellationToken)
                .RequireVersion(expectedVersion)
                .ConfigureAwait(false);
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
        var dbContact = await dbContext.Contacts
            .Get(id.Format(), cancellationToken)
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
}
