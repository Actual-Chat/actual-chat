using ActualChat.Search.Module;

namespace ActualChat.Search;

public sealed class ContactIndexingSignal : IAsyncDisposable
{
    private MomentClockSet Clocks { get; }
    private IMutableState<bool> NeedsSync { get; }
    private SearchSettings Settings { get; }

    private readonly ConcurrentTimerSet<Moment> _timers;

    public ContactIndexingSignal(IServiceProvider services)
    {
        Settings = services.GetRequiredService<SearchSettings>();
        _timers = new ConcurrentTimerSet<Moment>(ConcurrentTimerSetOptions.Default with {
                Quanta = Settings.ContactIndexingSignalInterval,
            },
            OnTimer);
        Clocks = services.Clocks();
        NeedsSync = services.StateFactory().NewMutable<bool>();
    }

    public ValueTask DisposeAsync()
        => _timers.DisposeAsync();

    public void SetDelayed()
    {
        var fireAt = Clocks.SystemClock.Now.ToLastIntervalStart(Settings.ContactIndexingSignalInterval)
            + Settings.ContactIndexingDelay
            + Settings.ContactIndexingSignalInterval;
        _timers.AddOrUpdate(fireAt, fireAt);
    }

    public void Reset()
        => NeedsSync.Value = false;

    public async Task WhenSet(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var cts = cancellationToken.CreateLinkedTokenSource();
        cts.CancelAfter(timeout);
        await NeedsSync.When(x => x, cts.Token).ConfigureAwait(false);
    }

    private void OnTimer(Moment time)
        => NeedsSync.Value = true;
}
