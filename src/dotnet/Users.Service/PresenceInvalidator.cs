namespace ActualChat.Users;

public class PresenceInvalidator : WorkerBase
{
    private readonly SortedCheckIns _awayQueue = new ();
    private readonly SortedCheckIns _offlineQueue = new ();
    private static readonly TimeSpan Eps = TimeSpan.FromMilliseconds(50);
    private Action<UserId> Callback { get; }
    private MomentClockSet Clocks { get; }
    private ILogger<PresenceInvalidator> Log { get; }
    private Moment Now => Clocks.SystemClock.Now;

    public PresenceInvalidator(
        Action<UserId> callback,
        MomentClockSet clocks,
        ILogger<PresenceInvalidator> log)
    {
        Callback = callback;
        Clocks = clocks;
        Log = log;
    }

    public bool HandleCheckIn(UserId userId, Moment lastCheckInAt)
    {
        var previous = _awayQueue.Set(userId, lastCheckInAt);
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
        while (!cancellationToken.IsCancellationRequested)
        {
            var earliest = queue.GetEarliest();
            if (earliest == null)
            {
                await Task.Delay(timeout, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var delay = timeout - TimeSince(earliest);
            if (delay > Eps)
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

            // invalidate only if user has not checked in since then
            if (queue.TryRemoveExact(earliest)) {
                Callback(earliest.UserId);
                nextQueue?.Set(earliest.UserId, earliest.At);
            }
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
