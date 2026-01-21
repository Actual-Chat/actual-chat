namespace ActualChat.Users.Db;

// ReSharper disable once TypeParameterCanBeVariant
public interface IDbUserRepo<TDbUser, TDbUserId>
    where TDbUser : DbUserBase<TDbUserId>, new()
    where TDbUserId : notnull
{
    public Type UserEntityType { get; }

    // Write methods
    public Task<TDbUser> Create(UsersDbContext dbContext, User user, CancellationToken cancellationToken = default);
    public Task<(TDbUser DbUser, bool IsCreated)> GetOrCreateOnSignIn(
        UsersDbContext dbContext, User user, CancellationToken cancellationToken = default);
    public Task Edit(
        UsersDbContext dbContext, TDbUser dbUser, Auth_EditUser command, CancellationToken cancellationToken = default);
    public Task Remove(
        UsersDbContext dbContext, TDbUser dbUser, CancellationToken cancellationToken = default);

    // Read methods
    public Task<TDbUser?> Get(TDbUserId userId, CancellationToken cancellationToken = default);
    public Task<TDbUser?> Get(UsersDbContext dbContext, TDbUserId userId, bool forUpdate, CancellationToken cancellationToken = default);
    public Task<TDbUser?> GetByUserIdentity(
        UsersDbContext dbContext, UserIdentity userIdentity, bool forUpdate, CancellationToken cancellationToken = default);
}
