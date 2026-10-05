using ActualChat.Users.Db;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Users;

/// <summary>
/// Backend service implementation for tracking user online/offline presence status.
/// </summary>
public class UserPresencesBackend(IServiceProvider services)
    : ShardedDbServiceBase<UsersDbContext>(services), IUserPresencesBackend
{
    // [ComputeMethod]
    public virtual async Task<Moment?> GetLastCheckIn(UserId userId, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var dbUserPresence = await dbContext.UserPresences
            .FirstOrDefaultAsync(x => x.UserId == userId.Value, cancellationToken)
            .ConfigureAwait(false);
        return dbUserPresence?.CheckInAt.ToMoment();
    }

    // [CommandHandler]
    public virtual async Task OnCheckIn(UserPresencesBackend_CheckIn command, CancellationToken cancellationToken)
    {
        // NB: command.At is effectively "now" here, see how it's set in UserPresences.OnCheckIn
        var (userId, now, isActive) = command;

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var awayTimeout = Constants.Presence.AwayTimeout;
        var dbUserPresence = await dbContext.UserPresences.ForUpdate()
            .FirstOrDefaultAsync(x => x.UserId == command.UserId.Value, cancellationToken)
            .ConfigureAwait(false);
        var isNewActiveDay = isActive
            && (dbUserPresence == null || UsageDay.DayOf(dbUserPresence.CheckInAt) < UsageDay.DayOf(now));
        if (dbUserPresence == null) {
            dbUserPresence = new DbUserPresence {
                UserId = userId.Value,
                IsActive = isActive,
                CheckInAt = isActive ? now : now - awayTimeout,
            };
            dbContext.Add(dbUserPresence);
        }
        else if (isActive) {
            dbUserPresence.IsActive = true;
            dbUserPresence.CheckInAt = now;
        }
        else {
            var lastCheckInRecency = now - dbUserPresence.CheckInAt.ToMoment();
            if (lastCheckInRecency <= 3 * awayTimeout)
                return; // Inactive & checked in recently -> Leave as-is

            // Inactive, but checked in 3*awayTimeout ago -> move CheckInAt to "away" range & mark inactive
            dbUserPresence.IsActive = false;
            dbUserPresence.CheckInAt = now - awayTimeout - TimeSpan.FromSeconds(1);
        }
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        Invalidation.Defer(() => _ = GetLastCheckIn(userId, default));

        if (isNewActiveDay) {
            var record = new UsageBackend_Record(userId, ApiArray.New(UsageEventSource.ActiveDay(now)));
            await Commander.Call(record, true, cancellationToken).ConfigureAwait(false);
        }
    }
}
