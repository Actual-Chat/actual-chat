using ActualChat.Users.Db;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

public class UserContactsBackend : DbServiceBase<UsersDbContext>, IUserContactsBackend
{
    private readonly IDbEntityResolver<string,DbUserContact> _dbUserContactResolver;

    public UserContactsBackend(IServiceProvider services) : base(services)
    {
        _dbUserContactResolver = Services.GetRequiredService<IDbEntityResolver<string, DbUserContact>>();
    }

    public virtual async Task<UserContact?> Get(string contactId, CancellationToken cancellationToken)
    {
        var dbContact = await _dbUserContactResolver.Get(contactId, cancellationToken).ConfigureAwait(false);
        return dbContact?.ToModel();
    }

    public virtual async Task<UserContact?> GetByTargetId(string ownerUserId, string targetPrincipalId, CancellationToken cancellationToken)
    {
        if (ownerUserId.IsNullOrEmpty() || targetPrincipalId.IsNullOrEmpty())
            return null;
        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);
        var dbContact = await dbContext.UserContacts
            .Where(a => a.OwnerUserId == ownerUserId && a.TargetUserId == targetPrincipalId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return dbContact?.ToModel();
    }

    public virtual async Task<string[]> GetContactIds(string userId, CancellationToken cancellationToken)
    {
        if (userId.IsNullOrEmpty())
            return Array.Empty<string>();

        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);
        var chatIds = await dbContext.UserContacts
            .Where(a => a.OwnerUserId == userId)
            .Select(a => a.Id)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        return chatIds;
    }

    public virtual async Task<bool> IsInContactList(string ownerUserId, string targetPrincipalId, CancellationToken cancellationToken)
        => await GetByTargetId(ownerUserId, targetPrincipalId, cancellationToken).ConfigureAwait(false) != null;

    public virtual async Task<UserContact> CreateContact(IUserContactsBackend.CreateContactCommand command, CancellationToken cancellationToken)
    {
        var contact = command.Contact;
        var ownerUserId = contact.OwnerUserId;
        if (Computed.IsInvalidating()) {
            _ = Get(ownerUserId, default);
            _ = GetContactIds(ownerUserId, default);
            var invUserContact = CommandContext.GetCurrent().Operation().Items.Get<UserContact>()!;
            _ = IsInContactList(invUserContact.OwnerUserId, invUserContact.TargetUserId, default);
            _ = Get(invUserContact.Id, default);
            return default!;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbUserContact = new DbUserContact() {
            Id = DbUserContact.GetCompositeId(ownerUserId, contact.TargetUserId),
            Version = VersionGenerator.NextVersion(),
            OwnerUserId = ownerUserId,
            TargetUserId = contact.TargetUserId,
            Name = contact.Name,
        };
        dbContext.UserContacts.Add(dbUserContact);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var userContact = dbUserContact.ToModel();
        CommandContext.GetCurrent().Operation().Items.Set(userContact);
        return userContact;
    }
}
