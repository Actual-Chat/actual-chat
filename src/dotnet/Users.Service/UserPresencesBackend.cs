using ActualChat.Users.Db;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

public class UserPresencesBackend : DbServiceBase<UsersDbContext>, IUserPresencesBackend, IAsyncDisposable
{
    private readonly PresenceTracker _presences;

    public UserPresencesBackend(IServiceProvider services) : base(services)
        => _presences = new(PresenceChanged, services.GetRequiredService<MomentClockSet>());

    public ValueTask DisposeAsync()
        => _presences.DisposeAsync();

    // [ComputeMethod]
    public virtual Task<Presence> Get(UserId userId, CancellationToken cancellationToken)
        => Task.FromResult(_presences.GetPresence(userId));

    // [CommandHandler]
    public virtual async Task CheckIn(IUserPresencesBackend.CheckInCommand command, CancellationToken cancellationToken)
    {
        var userId = command.UserId;
        var context = CommandContext.GetCurrent();
        if (Computed.IsInvalidating()) {
            var at = context.Operation().Items.GetOrDefault<Moment>();
            _presences.CheckIn(userId, at);
            return;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        // We handle check-in while invalidating to make sure they're in sync on every replica
        context.Operation().Items.Set(Clocks.SystemClock.Now);
    }

    // Private methods

    private void PresenceChanged(UserId userId)
    {
        if (Computed.IsInvalidating())
            _ = Get(userId, default);
        else
            using (Computed.Invalidate())
                _ = Get(userId, default);
    }
}
