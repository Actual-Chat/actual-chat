using ActualChat.Users.Db;
using Microsoft.Extensions.DependencyInjection;
using Stl.Fusion.EntityFramework;
using Stl.Fusion.EntityFramework.Authentication;

namespace ActualChat.Users;

public class UserInfoService : DbServiceBase<UsersDbContext>, IUserInfoService
{
    protected IServerSideAuthService Auth { get; }
    protected IDbUserRepo<UsersDbContext, DbUser, string> DbUsers { get; }
    protected IDbEntityResolver<string, DbUser> DbUserResolver { get; }
    protected DbUserByNameResolver DbUserByNameResolver { get; }
    protected IUserNameService UserNames { get; }

    public UserInfoService(IServiceProvider services) : base(services)
    {
        Auth = services.GetRequiredService<IServerSideAuthService>();
        DbUsers = services.GetRequiredService<IDbUserRepo<UsersDbContext, DbUser, string>>();
        DbUserResolver = services.DbEntityResolver<string, DbUser>();
        DbUserByNameResolver = services.GetRequiredService<DbUserByNameResolver>();
        UserNames = services.GetRequiredService<IUserNameService>();
    }

    public virtual async Task<UserInfo?> TryGet(UserId userId, CancellationToken cancellationToken)
    {
        var dbUser = await DbUserResolver.TryGet(userId, cancellationToken).ConfigureAwait(false);
        if (dbUser == null)
            return null;
        return new UserInfo(dbUser.Id, dbUser.Name);
    }

    public virtual async Task<UserInfo?> TryGetByName(string name, CancellationToken cancellationToken)
    {
        var dbUser = await DbUserByNameResolver.TryGet(name, cancellationToken).ConfigureAwait(false);
        if (dbUser == null)
            return null;
        return new UserInfo(dbUser.Id, dbUser.Name);
    }
}
