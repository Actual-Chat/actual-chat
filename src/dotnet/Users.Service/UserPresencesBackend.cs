using ActualChat.Users.Db;
using ActualChat.Users.Internal;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Users;

public class UserPresencesBackend : DbServiceBase<UsersDbContext>, IUserPresencesBackend, IAsyncDisposable
{
    private readonly UserPresenceTracker _userPresences;

    public UserPresencesBackend(IServiceProvider services) : base(services)
        => _userPresences = new(PresenceChanged, services.Clocks());

    public ValueTask DisposeAsync()
        => _userPresences.DisposeAsync();

    // [ComputeMethod]
    public virtual Task<Presence> Get(UserId userId, CancellationToken cancellationToken)
        => Task.FromResult(_userPresences.GetPresence(userId));

    // [ComputeMethod]
    public virtual async Task<Moment?> GetLastCheckIn(UserId userId, CancellationToken cancellationToken)
    {
        var lastCheckIn = _userPresences.GetLastCheckIn(userId);
        if (lastCheckIn != null)
            return lastCheckIn;

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var dbUserPresence = await dbContext.UserPresences
            .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken)
            .ConfigureAwait(false);
        return dbUserPresence?.CheckInAt.ToMoment();
    }

    // [CommandHandler]
    public virtual async Task OnCheckIn(UserPresencesBackend_CheckIn command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive) {
            _userPresences.CheckIn(command.UserId, command.At, command.IsActive);
            return;
        }

        var dbContext = await DbHub.CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbUserPresence = await dbContext.UserPresences.ForUpdate()
            .FirstOrDefaultAsync(x => x.UserId == command.UserId, cancellationToken)
            .ConfigureAwait(false);
        if (dbUserPresence == null) {
            dbUserPresence = new DbUserPresence {
                UserId = command.UserId,
                IsActive = command.IsActive,
                CheckInAt = command.At,
            };
            dbContext.Add(dbUserPresence);
        }
        else {
            dbUserPresence.IsActive = command.IsActive;
            dbUserPresence.CheckInAt = command.At;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private void PresenceChanged(UserId userId)
    {
        if (Invalidation.IsActive) {
            _ = Get(userId, default);
            _ = GetLastCheckIn(userId, default);
            return;
        }

        using (Invalidation.Begin()) {
            _ = Get(userId, default);
            _ = GetLastCheckIn(userId, default);
        }
    }
}
