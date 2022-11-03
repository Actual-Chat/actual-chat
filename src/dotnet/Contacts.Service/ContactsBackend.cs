using ActualChat.Chat;
using ActualChat.Commands;
using ActualChat.Contacts.Db;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Contacts;

public class ContactsBackend : DbServiceBase<ContactsDbContext>, IContactsBackend
{
    private IAccountsBackend AccountsBackend { get; }
    private IDbEntityResolver<string, DbContact> DbContactResolver { get; }
    private DiffEngine DiffEngine { get; }

    public ContactsBackend(IServiceProvider services) : base(services)
    {
        AccountsBackend = services.GetRequiredService<IAccountsBackend>();
        DbContactResolver = services.GetRequiredService<IDbEntityResolver<string, DbContact>>();
        DiffEngine = services.GetRequiredService<DiffEngine>();
    }

    public async Task<Contact> GetOrCreate(string ownerUserId, string targetUserId, CancellationToken cancellationToken)
    {
        var contact = await Get(ownerUserId, targetUserId, cancellationToken).ConfigureAwait(false);
        if (contact != null)
            return contact;

        var command = new IContactsBackend.ChangeCommand(Symbol.Empty, null, new Change<Contact> {
            Create = new Contact {
                OwnerId = ownerUserId,
                UserId = targetUserId,
            },
        });

        var newContact = await Commander.Call(command, false, cancellationToken).ConfigureAwait(false);
        return newContact;
    }

    // [ComputeMethod]
    public virtual async Task<Contact?> Get(string contactId, CancellationToken cancellationToken)
    {
        var dbContact = await DbContactResolver.Get(contactId, cancellationToken).ConfigureAwait(false);
        var contact = dbContact?.ToModel();
        if (contact == null)
            return null;

        var account = await AccountsBackend.Get(contact.UserId, cancellationToken).ConfigureAwait(false);
        if (account == null)
            return null;

        contact = contact with { Account = account };
        return contact;
    }

    // [ComputeMethod]
    public virtual async Task<Contact?> Get(string ownerUserId, string targetUserId, CancellationToken cancellationToken)
    {
        if (ownerUserId.IsNullOrEmpty() || targetUserId.IsNullOrEmpty())
            return null;

        var id = DbContact.ComposeUserContactId(ownerUserId, targetUserId);
        return await Get(id, cancellationToken).ConfigureAwait(false);

        // Old code:
        /*
        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);
        var dbContact = await dbContext.Contacts
            .Where(a => a.OwnerUserId == ownerUserId && a.TargetUserId == targetUserId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return dbContact?.ToModel();
        */
    }

    // [ComputeMethod]
    public virtual async Task<string[]> GetContactIds(string userId, CancellationToken cancellationToken)
    {
        if (userId.IsNullOrEmpty())
            return Array.Empty<string>();

        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);
        var contactIds = await dbContext.Contacts
            .Where(a => a.OwnerId == userId)
            .Select(a => a.Id)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        return contactIds;
    }

    [ComputeMethod]
    public virtual async Task<Contact?> GetPeerChatContact(
        string chatId, string ownerUserId,
        CancellationToken cancellationToken)
    {
        var parsedChatId = new ParsedChatId(chatId);
        switch (parsedChatId.Kind) {
        case ChatIdKind.PeerFull:
            break;
        case ChatIdKind.PeerShort:
            parsedChatId = chatId = ParsedChatId.FormatFullPeerChatId(ownerUserId, parsedChatId.UserId1);
            break;
        default: // Group or Invalid
            return null;
        }

        var (userId1, userId2) = (parsedChatId.UserId1.Id, parsedChatId.UserId2.Id);
        var targetUserId = (userId1, userId2).OtherThan((Symbol)ownerUserId);
        if (targetUserId.IsEmpty)
            throw StandardError.Constraint("Specified peer chat doesn't belong to the current user.");

        var contact = await Get(ownerUserId, targetUserId, cancellationToken).ConfigureAwait(false);
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
                _ = GetContactIds(invContact.OwnerId, default);
                if (!invContact.UserId.IsEmpty)
                    _ = Get(invContact.OwnerId, invContact.UserId, default);
                _ = Get(invContact.Id, default);
            }
            return default!;
        }

        change.RequireValid();
        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        DbContact? dbContact;
        if (change.IsCreate(out var contact)) {
            id.RequireEmpty("command.Id");
            contact = DiffEngine.Patch(new Contact(), contact);
            if (contact.OwnerId.IsEmpty)
                throw StandardError.Constraint("OwnerId is empty.");
            if (!contact.UserId.IsEmpty) {
                dbContact = await dbContext.Contacts
                    .FirstOrDefaultAsync(c =>
                            // ReSharper disable once AccessToModifiedClosure
                            c.OwnerId == contact.OwnerId.Value
                            // ReSharper disable once AccessToModifiedClosure
                            && c.UserId == contact.UserId.Value,
                        cancellationToken
                    ).ConfigureAwait(false);
            }
            else if (!contact.ChatId.IsEmpty) {
                dbContact = await dbContext.Contacts
                    .FirstOrDefaultAsync(c =>
                            // ReSharper disable once AccessToModifiedClosure
                            c.OwnerId == contact.OwnerId.Value
                            // ReSharper disable once AccessToModifiedClosure
                            && c.UserId == contact.ChatId.Value,
                        cancellationToken
                    ).ConfigureAwait(false);
            }
            else
                throw StandardError.Constraint("Neither UserId nor ChatId is set.");

            if (dbContact != null)
                return dbContact.ToModel(); // Already exist, so we don't recreate one
            dbContact = new DbContact(contact);
            dbContext.Add(dbContact);
        }
        else {
            // Update or Delete
            dbContact = await dbContext.Contacts
                .Get(id, cancellationToken)
                .RequireVersion(expectedVersion)
                .ConfigureAwait(false);
            if (change.IsUpdate(out contact))
                dbContact.UpdateFrom(contact);
            else
                dbContext.Remove(dbContact);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        contact = dbContact.ToModel();
        context.Operation().Items.Set(contact);

        if (change.IsCreate(out _)) {
            new IRecentEntriesBackend.UpdateCommand(
                RecencyScope.Contact,
                contact!.OwnerId,
                contact.Id,
                Clocks.SystemClock.UtcNow
                ).EnqueueOnCompletion(Queues.Users);
        }
        return contact;
    }
}
