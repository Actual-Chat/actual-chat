using ActualChat.Users.Db;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

public class UserPresences : DbServiceBase<UsersDbContext>, IUserPresences
{
    private IDbEntityResolver<string, DbUserPresence> DbUserPresenceResolver { get; }

    public UserPresences(IServiceProvider services)
        : base(services)
        => DbUserPresenceResolver = services.DbEntityResolver<string, DbUserPresence>();

    [ComputeMethod(AutoInvalidationDelay = 61)]
    public virtual async Task<Presence> Get(string userId, CancellationToken cancellationToken)
    {
        var cutoffTime = Clocks.SystemClock.Now - TimeSpan.FromMinutes(1);
        var userState = await DbUserPresenceResolver.Get(userId, cancellationToken).ConfigureAwait(false);
        return userState?.OnlineCheckInAt > cutoffTime ? Presence.Online : Presence.Offline;
    }
}
