namespace ActualChat.Users;

public class UserPresencesBackend : IUserPresencesBackend, IDisposable
{
    private readonly PresenceInvalidator _presenceInvalidator;
    private CheckIns CheckIns { get; }
    private MomentClockSet Clocks { get; }

    public UserPresencesBackend(CheckIns checkIns, MomentClockSet clocks, IServiceProvider services)
    {
        CheckIns = checkIns;
        Clocks = clocks;

        _presenceInvalidator = new PresenceInvalidator(Invalidate, Clocks, services.LogFor<PresenceInvalidator>());
        _presenceInvalidator.Start();
    }

    public void Dispose()
        => _presenceInvalidator.Dispose();

    // [ComputeMethod]
    public virtual Task<Presence> Get(UserId userId, CancellationToken cancellationToken)
        => Task.FromResult(GetPresence(userId));

    // [CommandHandler]
    public virtual Task CheckIn(IUserPresencesBackend.CheckInCommand command, CancellationToken cancellationToken)
    {
        var userId = command.UserId;
        var context = CommandContext.GetCurrent();
        if (Computed.IsInvalidating()) {
            // invalidate only if new to become online
            if (context.Operation().Items.GetOrDefault(false))
                _ = Get(command.UserId, default);
            return Task.CompletedTask; // It just spawns other commands, so nothing to do here
        }

        var lastCheckInAt = Clocks.SystemClock.Now;
        CheckIns.Set(userId, lastCheckInAt);
        var needsInvalidate = _presenceInvalidator.HandleCheckIn(userId, lastCheckInAt);
        context.Operation().Items.Set(needsInvalidate);

        return Task.CompletedTask;
    }


    // private

    private Presence GetPresence(UserId userId)
    {
        var lastCheckIn = CheckIns.Get(userId);
        if (lastCheckIn == null)
            return Presence.Unknown;

        if (Clocks.SystemClock.Now - lastCheckIn < Constants.Presence.AwayTimeout)
            return Presence.Online;

        if (Clocks.SystemClock.Now - lastCheckIn < Constants.Presence.OfflineTimeout)
            return Presence.Away;

        return Presence.Offline;
    }

    private void Invalidate(IReadOnlyList<UserId> userIds)
    {
        using (Computed.Invalidate())
            foreach (var userId in userIds)
                _ = Get(userId, default);
    }
}
