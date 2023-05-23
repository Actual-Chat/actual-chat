using ActualChat.Users.Db;
using ActualChat.Users.Internal;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

public class UserPresencesBackend : DbServiceBase<UsersDbContext>, IUserPresencesBackend, IAsyncDisposable
{
    private readonly UserPresenceTracker _userPresences;

    public UserPresencesBackend(IServiceProvider services) : base(services)
        => _userPresences = new(PresenceChanged, services.GetRequiredService<MomentClockSet>());

    public ValueTask DisposeAsync()
        => _userPresences.DisposeAsync();

    // [ComputeMethod]
    public virtual Task<Presence> Get(UserId userId, CancellationToken cancellationToken)
        => Task.FromResult(_userPresences.GetPresence(userId));

    // [CommandHandler]
    public virtual async Task CheckIn(IUserPresencesBackend.CheckInCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating()) {
            _userPresences.CheckIn(command.UserId, command.At, command.IsActive);
            return;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);
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
