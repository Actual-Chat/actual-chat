using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Users.Db;

public class DbUserRepoBase<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TDbUser,
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TDbUserId>(
    AuthBackend.Options settings,
    IServiceProvider services
    ) : DbServiceBase<UsersDbContext>(services), IDbUserRepo<TDbUser, TDbUserId>
    where TDbUser : DbUserBase<TDbUserId>, new()
    where TDbUserId : notnull
{
    protected AuthBackend.Options Settings { get; init; } = settings;
    protected IDbUserIdHandler<TDbUserId> DbUserIdHandler { get; init; }
        = services.GetRequiredService<IDbUserIdHandler<TDbUserId>>();
    protected IDbEntityResolver<TDbUserId, TDbUser> UserResolver { get; init; }
        = services.DbEntityResolver<TDbUserId, TDbUser>();
    protected IDbEntityConverter<TDbUser, User> UserConverter { get; init; }
        = services.DbEntityConverter<TDbUser, User>();

    public Type UserEntityType => typeof(TDbUser);

    // Write methods

    public virtual async Task<TDbUser> Create(
        UsersDbContext dbContext, User user, CancellationToken cancellationToken = default)
    {
        // Creating "base" dbUser
        var id = DbUserIdHandler.Parse(user.Id, true);
        if (DbUserIdHandler.IsNone(id))
            id = DbUserIdHandler.New();
        var dbUser = new TDbUser() {
            Id = id,
            Version = VersionGenerator.NextVersion(),
            Name = user.Name,
            Claims = user.Claims.ToImmutableDictionary(StringComparer.Ordinal),
        };
        dbContext.Add(dbUser);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        user = user with {
            Id = DbUserIdHandler.Format(dbUser.Id)
        };
        // Updating dbUser from the model to persist user.Identities
        UserConverter.UpdateEntity(user, dbUser);
        dbContext.Update(dbUser);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return dbUser;
    }

    public virtual async Task<(TDbUser DbUser, bool IsCreated)> GetOrCreateOnSignIn(
        UsersDbContext dbContext, User user, CancellationToken cancellationToken = default)
    {
        var dbUserId = DbUserIdHandler.Parse(user.Id, true);
        TDbUser? dbUser;
        if (!DbUserIdHandler.IsNone(dbUserId)) {
            dbUser = await Get(dbContext, dbUserId, false, cancellationToken).ConfigureAwait(false);
            if (dbUser is not null)
                return (dbUser, false);
        }

        // No user found, let's create it
        dbUser = await Create(dbContext, user, cancellationToken).ConfigureAwait(false);
        return (dbUser, true);
    }

    public virtual async Task Edit(UsersDbContext dbContext, TDbUser dbUser, AuthBackend_EditUser command,
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
        UsersDbContext dbContext, TDbUser dbUser, CancellationToken cancellationToken = default)
    {
        await dbContext.Entry(dbUser).Collection(nameof(DbUserBase<object>.Identities))
            .LoadAsync(cancellationToken).ConfigureAwait(false);
        if (dbUser.Identities.Count > 0)
            dbContext.RemoveRange(dbUser.Identities);
        dbContext.Remove(dbUser);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    // Read methods

    public async Task<TDbUser?> Get(TDbUserId userId, CancellationToken cancellationToken = default)
        => await UserResolver.Get(DbShard.Single, userId, cancellationToken).ConfigureAwait(false);

    public virtual async Task<TDbUser?> Get(
        UsersDbContext dbContext, TDbUserId userId, bool forUpdate, CancellationToken cancellationToken = default)
    {
        var dbUsers = forUpdate
            ? dbContext.Set<TDbUser>().ForNoKeyUpdate()
            : dbContext.Set<TDbUser>();
        var dbUser = await dbUsers
            .FirstOrDefaultAsync(u => Equals(u.Id, userId), cancellationToken)
            .ConfigureAwait(false);
        if (dbUser is not null)
            await dbContext.Entry(dbUser).Collection(nameof(DbUserBase<object>.Identities))
                .LoadAsync(cancellationToken).ConfigureAwait(false);
        return dbUser;
    }

    public virtual async Task<TDbUser?> GetByUserIdentity(
        UsersDbContext dbContext, UserIdentity userIdentity, bool forUpdate, CancellationToken cancellationToken = default)
    {
        if (!userIdentity.IsValid)
            return null;

        var dbUserIdentities = forUpdate
            ? dbContext.Set<DbUserIdentity<TDbUserId>>().ForNoKeyUpdate()
            : dbContext.Set<DbUserIdentity<TDbUserId>>();
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
