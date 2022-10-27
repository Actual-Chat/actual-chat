using ActualChat.Users.Db;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;
using Stl.Versioning;

namespace ActualChat.Users;

public class UserContactsBackend : DbServiceBase<UsersDbContext>, IUserContactsBackend
{
    private IAccountsBackend AccountsBackend { get; }
    private IAvatarsBackend AvatarsBackend { get; }
    private IDbEntityResolver<string, DbUserContact> DbUserContactResolver { get; }
    private DiffEngine DiffEngine { get; }

    public UserContactsBackend(IServiceProvider services) : base(services)
    {
        AccountsBackend = services.GetRequiredService<IAccountsBackend>();
        AvatarsBackend = services.GetRequiredService<IAvatarsBackend>();
        DbUserContactResolver = services.GetRequiredService<IDbEntityResolver<string, DbUserContact>>();
        DiffEngine = services.GetRequiredService<DiffEngine>();
    }

    public async Task<UserContact> GetOrCreate(string ownerUserId, string targetUserId, CancellationToken cancellationToken)
    {
        var contact = await Get(ownerUserId, targetUserId, cancellationToken).ConfigureAwait(false);
        if (contact != null)
            return contact;
        var command = new IUserContactsBackend.ChangeCommand(Symbol.Empty, null, new Change<UserContactDiff> {
            Create = new UserContactDiff {
                OwnerUserId = ownerUserId,
                TargetUserId = targetUserId,
            },
        });

        return await Commander.Call(command, true, cancellationToken).Require().ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<UserContact?> Get(string contactId, CancellationToken cancellationToken)
    {
        var dbContact = await DbUserContactResolver.Get(contactId, cancellationToken).ConfigureAwait(false);
        var contact = dbContact?.ToModel();
        if (contact == null)
            return null;

        var account = await AccountsBackend.Get(contact.Id, cancellationToken).ConfigureAwait(false);
        if (account == null)
            return null;

        var avatar = await AvatarsBackend.Get(account.AvatarId, cancellationToken).ConfigureAwait(false);
        contact = contact with { Avatar = avatar };
        return contact;
    }

    // [ComputeMethod]
    public virtual async Task<UserContact?> Get(string ownerUserId, string targetUserId, CancellationToken cancellationToken)
    {
        if (ownerUserId.IsNullOrEmpty() || targetUserId.IsNullOrEmpty())
            return null;

        var contactId = DbUserContact.ComposeId(ownerUserId, targetUserId);
        return await Get(contactId, cancellationToken).ConfigureAwait(false);

        // Old code:
        /*
        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);
        var dbContact = await dbContext.UserContacts
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
        var contactIds = await dbContext.UserContacts
            .Where(a => a.OwnerUserId == userId)
            .Select(a => a.Id)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        return contactIds;
    }

    // [ComputeMethod]
    public virtual async Task<string> GetPeerChatName(string targetUserId, CancellationToken cancellationToken)
    {
        var userAuthor = await AccountsBackend.GetUserAuthor(targetUserId, cancellationToken)
            .Require()
            .ConfigureAwait(false);
        return userAuthor.Name;
    }

    // [CommandHandler]
    public virtual async Task<UserContact?> Change(
        IUserContactsBackend.ChangeCommand command,
        CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();
        var (id, expectedVersion, change) = command;
        if (Computed.IsInvalidating()) {
            var invUserContact = context.Operation().Items.Get<UserContact>();
            if (invUserContact != null) {
                _ = GetContactIds(invUserContact.OwnerUserId, default);
                _ = Get(invUserContact.OwnerUserId, invUserContact.TargetUserId, default);
                _ = Get(invUserContact.Id, default);
            }
            return default;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        UserContact? userContact;
        DbUserContact? dbUserContact;
        if (change.RequireValid().IsCreate(out var update)) {
            id.RequireEmpty("Command.Id");
            userContact = new UserContact { Id = id };
            userContact = DiffEngine.Patch(userContact, update);
            dbUserContact = new DbUserContact(userContact);
            dbContext.Add(dbUserContact);
        }
        else {
            dbUserContact = await dbContext.UserContacts.Get(id, cancellationToken).Require().ConfigureAwait(false);
            userContact = dbUserContact.ToModel();
            VersionChecker.RequireExpected(userContact.Version, expectedVersion);

            if (change.IsUpdate(out update)) {
                id.RequireNonEmpty("Command.Id");
                userContact = DiffEngine.Patch(userContact, update);
                dbUserContact.UpdateFrom(userContact);
                dbContext.Update(dbUserContact);
            }
            else {
                dbContext.Remove(dbUserContact);
                dbUserContact = null;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        userContact = dbUserContact?.ToModel();
        context.Operation().Items.Set(userContact);

        if (change.IsCreate(out _))
            await Commander.Call(new IRecentEntriesBackend.UpdateCommand(
                        RecencyScope.UserContact,
                        userContact!.OwnerUserId,
                        userContact.Id,
                        Clocks.SystemClock.UtcNow),
                    cancellationToken)
                .ConfigureAwait(false);

        return userContact;
    }
}
