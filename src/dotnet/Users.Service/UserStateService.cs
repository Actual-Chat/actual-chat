using ActualChat.Users.Db;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

public class UserStateService : DbServiceBase<UsersDbContext>, IUserStateService
{
    protected IDbEntityResolver<string, DbUserState> DbUserStateResolver { get; }

    public UserStateService(IServiceProvider services)
        : base(services)
        => DbUserStateResolver = services.DbEntityResolver<string, DbUserState>();

    [ComputeMethod(AutoInvalidateTime = 61)]
    public virtual async Task<bool> IsOnline(UserId userId, CancellationToken cancellationToken)
    {
        var cutoffTime = Clocks.SystemClock.Now - TimeSpan.FromMinutes(1);
        var userState = await DbUserStateResolver.TryGet(userId, cancellationToken);
        return userState?.OnlineCheckInAt > cutoffTime;
    }
}
