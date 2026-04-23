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
        // !!! command is IDelegatingCommand, so no invalidation block here
        var (userId, checkInAt, isActive) = command;
        var context = CommandContext.GetCurrent();

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);
        context.Operation.MustStore(false);

        var dbUserPresence = await dbContext.UserPresences.ForUpdate()
            .FirstOrDefaultAsync(x => x.UserId == command.UserId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (dbUserPresence == null) {
            dbUserPresence = new DbUserPresence {
                UserId = command.UserId.Value,
                IsActive = isActive,
                CheckInAt = checkInAt,
            };
            dbContext.Add(dbUserPresence);
        }
        else {
            dbUserPresence.IsActive = command.IsActive;
            dbUserPresence.CheckInAt = command.At;
        }
        context.Operation.AddCompletionHandler(scope => {
            using (Invalidation.Begin())
                _ = GetLastCheckIn(userId, default);
            return Task.CompletedTask;
        });
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
