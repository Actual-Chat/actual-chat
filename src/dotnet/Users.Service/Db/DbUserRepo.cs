using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ActualChat.Users.Module;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Users.Db;

public class DbUserRepo(
    AuthBackend.Options settings,
    IServiceProvider services
    ) : DbServiceBase<UsersDbContext>(services)
{
    private IDbEntityResolver<string, DbUser> UserResolver { get; init; }
        = services.DbEntityResolver<string, DbUser>();

    private UsersSettings UsersSettings { get; } = services.GetRequiredService<UsersSettings>();

    // Write methods

    public virtual async Task<DbUser> Create(
        UsersDbContext dbContext, User user, CancellationToken cancellationToken = default)
    {
        // Creating "base" dbUser
        var dbUser = new DbUser() {
            Id = user.Id.NullIfEmpty() ?? UserId.New().Value,
            Version = VersionGenerator.NextVersion(),
            Name = user.Name,
            Claims = user.Claims.ToImmutableDictionary(StringComparer.Ordinal),
        };
        dbContext.Add(dbUser);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        user = user with {
            Id = dbUser.Id ?? ""
        };
        // Updating dbUser from the model to persist user.Identities
        dbUser.UpdateFrom(user, VersionGenerator);
        dbContext.Update(dbUser);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // ActualChat-specific: Create DbAccount for new user
        user = dbUser.ToModel();

        var context = CommandContext.GetCurrent();
        var isAdmin = AccountsBackend.IsAdmin(user);
        var name = user.Claims.GetValueOrDefault(ClaimTypes.GivenName, "");
        var lastName = user.Claims.GetValueOrDefault(ClaimTypes.Surname, "");
        if (!lastName.IsNullOrEmpty())
            name = $"{name} {lastName}";
        var dbAccount = new DbAccount {
            Id = user.Id,
            Status = isAdmin ? AccountStatus.Active : UsersSettings.NewAccountStatus,
            Version = VersionGenerator.NextVersion(),
            Name = name,
            Email = user.Claims.GetValueOrDefault(ClaimTypes.Email, ""),
            Phone = user.Claims.GetValueOrDefault(ClaimTypes.MobilePhone, ""),
            CreatedAt = dbUser.CreatedAt,
        };
        dbContext.Accounts.Add(dbAccount);

        var emailString = dbAccount.Email;
        if (!emailString.IsNullOrEmpty() && ActualChat.Email.TryParse(emailString, out var email)) {
            user = user.WithEmailIdentities(email);
            dbUser.UpdateFrom(user, VersionGenerator);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation.AddEvent(
            new AccountChangedEvent(dbAccount.ToModel(user), null, ChangeKind.Create));
        return dbUser;
    }

    public virtual async Task<(DbUser DbUser, bool IsCreated)> GetOrCreateOnSignIn(
        UsersDbContext dbContext, User user, CancellationToken cancellationToken = default)
    {
        DbUser? dbUser;
        if (!user.Id.IsNullOrEmpty()) {
            dbUser = await Get(dbContext, user.Id, false, cancellationToken).ConfigureAwait(false);
            if (dbUser is not null)
                return (dbUser, false);
        }

        // No user found, let's create it
        dbUser = await Create(dbContext, user, cancellationToken).ConfigureAwait(false);
        return (dbUser, true);
    }

    public virtual async Task Edit(UsersDbContext dbContext, DbUser dbUser, AuthBackend_EditUser command,
        CancellationToken cancellationToken = default)
    {
        if (command.Name is not null) {
            dbUser.Name = command.Name;
            dbUser.Version = VersionGenerator.NextVersion(dbUser.Version);
        }
        dbContext.Update(dbUser);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task Remove(
        UsersDbContext dbContext, DbUser dbUser, CancellationToken cancellationToken = default)
    {
        await dbContext.Entry(dbUser).Collection(nameof(DbUser.Identities))
            .LoadAsync(cancellationToken).ConfigureAwait(false);
        if (dbUser.Identities.Count > 0)
            dbContext.RemoveRange(dbUser.Identities);
        dbContext.Remove(dbUser);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    // Read methods

    public async Task<DbUser?> Get(string userId, CancellationToken cancellationToken = default)
        => await UserResolver.Get(DbShard.Single, userId, cancellationToken).ConfigureAwait(false);

    public virtual async Task<DbUser?> Get(
        UsersDbContext dbContext, string userId, bool forUpdate, CancellationToken cancellationToken = default)
    {
        var dbUsers = forUpdate
            ? dbContext.Set<DbUser>().ForNoKeyUpdate()
            : dbContext.Set<DbUser>();
        var dbUser = await dbUsers
            .FirstOrDefaultAsync(u => Equals(u.Id, userId), cancellationToken)
            .ConfigureAwait(false);
        if (dbUser is not null)
            await dbContext.Entry(dbUser).Collection(nameof(DbUser.Identities))
                .LoadAsync(cancellationToken).ConfigureAwait(false);
        return dbUser;
    }

    public virtual async Task<DbUser?> GetByUserIdentity(
        UsersDbContext dbContext, UserIdentity userIdentity, bool forUpdate, CancellationToken cancellationToken = default)
    {
        if (!userIdentity.IsValid)
            return null;

        var dbUserIdentities = forUpdate
            ? dbContext.Set<DbUserIdentity<string>>().ForNoKeyUpdate()
            : dbContext.Set<DbUserIdentity<string>>();
        var id = userIdentity.Id;
        var dbUserIdentity = await dbUserIdentities
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (dbUserIdentity is null)
            return null;

        var user = await Get(dbContext, dbUserIdentity.DbUserId, forUpdate, cancellationToken).ConfigureAwait(false);
        return user;
    }
}
