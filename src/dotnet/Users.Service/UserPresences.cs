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
        var minCheckInTime = Clocks.SystemClock.Now - Constants.Presence.CheckInTimeout;
        var dbUserPresence = await DbUserPresenceResolver.Get(userId, cancellationToken).ConfigureAwait(false);
        if (dbUserPresence == null)
            return Presence.Offline;
        return dbUserPresence.OnlineCheckInAt.ToMoment() >= minCheckInTime ? Presence.Online : Presence.Offline;
    }
}
