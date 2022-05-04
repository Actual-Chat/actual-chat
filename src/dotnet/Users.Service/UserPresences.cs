using ActualChat.Users.Db;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

public class UserPresences : DbServiceBase<UsersDbContext>, IUserPresences
{
    protected IDbEntityResolver<string, DbUserPresence> DbUserStateResolver { get; }

    public UserPresences(IServiceProvider services)
        : base(services)
        => DbUserStateResolver = services.DbEntityResolver<string, DbUserPresence>();

    [ComputeMethod(AutoInvalidateTime = 61)]
    public virtual async Task<Presence> Get(string userId, CancellationToken cancellationToken)
    {
        var cutoffTime = Clocks.SystemClock.Now - TimeSpan.FromMinutes(1);
        var userState = await DbUserStateResolver.Get(userId, cancellationToken).ConfigureAwait(false);
        return userState?.OnlineCheckInAt > cutoffTime ? Presence.Online : Presence.Offline;
    }
}
