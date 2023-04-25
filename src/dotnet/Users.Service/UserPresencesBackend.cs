using ActualChat.Users.Db;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

public class UserPresencesBackend : DbServiceBase<UsersDbContext>, IUserPresencesBackend, IAsyncDisposable
{
    private readonly PresenceInvalidator _presenceInvalidator;

    public UserPresencesBackend(IServiceProvider services) : base(services)
        => _presenceInvalidator = new (Invalidate, services.GetRequiredService<MomentClockSet>());

    public ValueTask DisposeAsync()
        => _presenceInvalidator.DisposeAsync();

    // [ComputeMethod]
    public virtual Task<Presence> Get(UserId userId, CancellationToken cancellationToken)
        => Task.FromResult(_presenceInvalidator.GetPresence(userId));

    // [CommandHandler]
    public virtual async Task CheckIn(IUserPresencesBackend.CheckInCommand command, CancellationToken cancellationToken)
    {
        var userId = command.UserId;
        var context = CommandContext.GetCurrent();
        if (Computed.IsInvalidating()) {
            // for replica sync
            var at = context.Operation().Items.GetOrDefault<Moment>();
            var mustInvalidate = _presenceInvalidator.HandleCheckIn(userId, at);
            if (mustInvalidate)
                _ = Get(command.UserId, default);
            return;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        // we handle check while invalidating so it is handled on every replica
        context.Operation().Items.Set(Clocks.SystemClock.Now);
    }

    // Private methods

    private void Invalidate(UserId userId)
    {
        using (Computed.Invalidate())
            _ = Get(userId, default);
    }
}
