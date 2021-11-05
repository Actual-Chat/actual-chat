using ActualChat.Users.Db;
using Microsoft.Extensions.DependencyInjection;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

public class UserInfosService : DbServiceBase<UsersDbContext>, IUserInfos
{
    private readonly IDbEntityResolver<string, DbUser> _dbUserResolver;
    private readonly DbUserByNameResolver _dbUserByNameResolver;

    public UserInfosService(IServiceProvider services) : base(services)
    {
        _dbUserResolver = services.DbEntityResolver<string, DbUser>();
        _dbUserByNameResolver = services.GetRequiredService<DbUserByNameResolver>();
    }

    public virtual async Task<UserInfo?> Get(UserId userId, CancellationToken cancellationToken)
    {
        var dbUser = await _dbUserResolver.Get(userId, cancellationToken).ConfigureAwait(false);
        return dbUser == null ? null : new UserInfo(dbUser.Id, dbUser.Name);
    }

    public virtual async Task<UserInfo?> GetByName(string name, CancellationToken cancellationToken)
    {
        var dbUser = await _dbUserByNameResolver.Get(name, cancellationToken).ConfigureAwait(false);
        return dbUser == null ? null : new UserInfo(dbUser.Id, dbUser.Name);
    }
}
