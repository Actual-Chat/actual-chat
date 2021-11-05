using ActualChat.Users.Db;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

public class UserStatesService : DbServiceBase<UsersDbContext>, IUserStates
{
    protected IDbEntityResolver<string, DbUserState> DbUserStateResolver { get; }

    public UserStatesService(IServiceProvider services)
        : base(services)
        => DbUserStateResolver = services.DbEntityResolver<string, DbUserState>();

    [ComputeMethod(AutoInvalidateTime = 61)]
    public virtual async Task<bool> IsOnline(UserId userId, CancellationToken cancellationToken)
    {
        var cutoffTime = Clocks.SystemClock.Now - TimeSpan.FromMinutes(1);
        var userState = await DbUserStateResolver.Get(userId, cancellationToken).ConfigureAwait(false);
        return userState?.OnlineCheckInAt > cutoffTime;
    }
}
