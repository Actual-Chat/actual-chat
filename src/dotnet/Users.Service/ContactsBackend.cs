using ActualChat.Commands;
using ActualChat.Users.Db;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;
using Stl.Versioning;

namespace ActualChat.Users;

public class ContactsBackend : DbServiceBase<UsersDbContext>, IContactsBackend
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
        var command = new IContactsBackend.ChangeCommand(Symbol.Empty, null, new Change<ContactDiff> {
            Create = new ContactDiff {
                OwnerUserId = ownerUserId,
                TargetUserId = targetUserId,
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

        var account = await AccountsBackend.Get(contact.Id, cancellationToken).ConfigureAwait(false);
        if (account == null)
            return null;

        contact = contact with {
            Avatar = account.Avatar,
        };
        return contact;
    }

    // [ComputeMethod]
    public virtual async Task<Contact?> Get(string ownerUserId, string targetUserId, CancellationToken cancellationToken)
    {
        if (ownerUserId.IsNullOrEmpty() || targetUserId.IsNullOrEmpty())
            return null;

        var id = DbContact.ComposeId(ownerUserId, targetUserId);
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
            .Where(a => a.OwnerUserId == userId)
            .Select(a => a.Id)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        return contactIds;
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
                _ = GetContactIds(invContact.OwnerUserId, default);
                _ = Get(invContact.OwnerUserId, invContact.TargetUserId, default);
                _ = Get(invContact.Id, default);
            }
            return default!;
        }

        change.RequireValid();
        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        Contact? contact;
        DbContact? dbContact;
        if (change.IsCreate(out var contactDiff)) {
            id.RequireEmpty("command.Id");
            contact = DiffEngine.Patch(new Contact(), contactDiff);
            if (contact.OwnerUserId.IsEmpty)
                throw StandardError.Constraint("OwnerUserId is empty.");
            if (contact.TargetUserId.IsEmpty)
                throw StandardError.Constraint("TargetUserId is empty.");

            dbContact = await dbContext.Contacts
                .FirstOrDefaultAsync(c =>
                    // ReSharper disable once AccessToModifiedClosure
                    c.OwnerUserId == contact.OwnerUserId.Value
                    // ReSharper disable once AccessToModifiedClosure
                    && c.TargetUserId == contact.TargetUserId.Value,
                    cancellationToken
                ).ConfigureAwait(false);
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
            contact = dbContact.ToModel();

            if (change.IsUpdate(out contactDiff)) {
                contact = DiffEngine.Patch(contact, contactDiff);
                dbContact.UpdateFrom(contact);
            }
            else
                dbContext.Remove(dbContact);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        contact = dbContact.ToModel();
        context.Operation().Items.Set(contact);

        if (change.IsCreate(out _)) {
            new IRecentEntriesBackend.UpdateCommand(
                RecencyScope.Contact,
                contact!.OwnerUserId,
                contact.Id,
                Clocks.SystemClock.UtcNow
                ).EnqueueOnCompletion(Queues.Users);
        }
        return contact;
    }
}
