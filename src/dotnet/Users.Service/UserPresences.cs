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
    public virtual async Task<Presence> Get(UserId userId, CancellationToken cancellationToken)
    {
        var dbUserPresence = await DbUserPresenceResolver.Get(userId, cancellationToken).ConfigureAwait(false);
        if (dbUserPresence == null)
            return Presence.Offline;

        var inactiveFor = Clocks.SystemClock.Now - dbUserPresence.OnlineCheckInAt.ToMoment();
        if (inactiveFor > Constants.Presence.OfflineTimeout)
            return Presence.Offline;

        if (inactiveFor > Constants.Presence.AwayTimeout)
            return Presence.Away;

        return Presence.Online;
    }
}
