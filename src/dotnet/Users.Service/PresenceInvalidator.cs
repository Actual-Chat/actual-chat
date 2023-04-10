namespace ActualChat.Users;

public class PresenceInvalidator : WorkerBase
{
    private readonly SortedCheckIns _awayQueue = new ();
    private readonly SortedCheckIns _offlineQueue = new ();
    private Action<IReadOnlyList<UserId>> Callback { get; }
    private MomentClockSet Clocks { get; }
    private ILogger<PresenceInvalidator> Log { get; }
    private Moment Now => Clocks.SystemClock.Now;

    public PresenceInvalidator(
        Action<IReadOnlyList<UserId>> callback,
        MomentClockSet clocks,
        ILogger<PresenceInvalidator> log)
    {
        Callback = callback;
        Clocks = clocks;
        Log = log;
    }

    public bool HandleCheckIn(UserId userId, Moment lastCheckInAt)
    {
        var previous = _awayQueue.Set(new (userId, lastCheckInAt));
        return IsOutdated(previous);
    }

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        var baseChains = new AsyncChain[] {
            new (nameof(InvalidateOnAway), InvalidateOnAway),
            new (nameof(InvalidateOnOffline), InvalidateOnOffline),
        };
        var retryDelays = new RetryDelaySeq(1, 10);
        return (
            from chain in baseChains
            select chain
                .RetryForever(retryDelays, Log)
                .Log(LogLevel.Debug, Log)
            ).RunIsolated(cancellationToken);
    }

    private Task InvalidateOnAway(CancellationToken cancellationToken)
        => InvalidateOnTimeout(_awayQueue, _offlineQueue, Constants.Presence.AwayTimeout, cancellationToken);

    private Task InvalidateOnOffline(CancellationToken cancellationToken)
        => InvalidateOnTimeout(_offlineQueue, null, Constants.Presence.OfflineTimeout, cancellationToken);

    private async Task InvalidateOnTimeout(SortedCheckIns queue, SortedCheckIns? nextQueue, TimeSpan timeout, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested) {
            var toInvalidate = queue.PopRange(Now - timeout);
            if (toInvalidate.Count == 0)
            {
                await Task.Delay(timeout, cancellationToken).ConfigureAwait(false);
                continue;
            }

            // invalidate only if user has not checked in since then
            Callback(toInvalidate.Select(x => x.UserId).ToList());
        }
    }

    private bool IsOutdated(UserCheckIn? checkIn)
    {
        if (checkIn == null)
            return true;

        var timeSinceLastCheckIn = TimeSince(checkIn);
        // TODO: timeout from settings
        return timeSinceLastCheckIn >= Constants.Presence.AwayTimeout
            || timeSinceLastCheckIn >= Constants.Presence.OfflineTimeout;
    }

    private TimeSpan TimeSince(UserCheckIn checkIn)
        => Now - checkIn.At;
}
