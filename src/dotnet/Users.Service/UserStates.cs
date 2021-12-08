using ActualChat.Users.Db;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

public class UserStates : DbServiceBase<UsersDbContext>, IUserStates
{
    protected IDbEntityResolver<string, DbUserState> DbUserStateResolver { get; }

    public UserStates(IServiceProvider services)
        : base(services)
        => DbUserStateResolver = services.DbEntityResolver<string, DbUserState>();

    [ComputeMethod(AutoInvalidateTime = 61)]
    public virtual async Task<bool> IsOnline(string userId, CancellationToken cancellationToken)
    {
        var cutoffTime = Clocks.SystemClock.Now - TimeSpan.FromMinutes(1);
        var userState = await DbUserStateResolver.Get(userId, cancellationToken).ConfigureAwait(false);
        return userState?.OnlineCheckInAt > cutoffTime;
    }
}
