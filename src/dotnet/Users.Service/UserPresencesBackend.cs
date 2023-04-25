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
            _presenceInvalidator.Set(userId, context.Operation().Items.GetOrDefault<Moment>());
            // invalidate only if new to become online
            if (context.Operation().Items.GetOrDefault(false)) {
                _ = Get(command.UserId, default);
            }
            return; // It just spawns other commands, so nothing to do here
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var (mustInvalidate, at) = _presenceInvalidator.HandleCheckIn(userId);
        context.Operation().Items.Set(mustInvalidate);
        context.Operation().Items.Set(at);
    }

    // Private methods

    private void Invalidate(UserId userId)
    {
        using (Computed.Invalidate())
            _ = Get(userId, default);
    }
}
