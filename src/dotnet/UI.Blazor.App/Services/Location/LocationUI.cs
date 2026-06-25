using ActualChat.Kvas;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Drives the local user's live-location shares: while at least one chat is being shared it runs a
/// <see cref="FuncWorker"/> that starts the platform <see cref="ILocationTracker"/> and pushes
/// <see cref="LiveLocations_Report"/> to every shared chat once per <see cref="Constants.LiveLocation.UpdatePeriod"/>.
/// The worker exists only while there are shares; shares are persisted locally and resumed on restart.
/// </summary>
public class LocationUI : UIWorkerBase<AppUIHub>, IComputeService
{
    private readonly StoredState<ActiveShare[]> _shares;

    private ILocationTracker Tracker => field ??= Hub.Services.GetRequiredService<ILocationTracker>();
    private Moment ServerNow => Clocks.ServerClock.Now;

    public LocationUI(AppUIHub hub) : base(hub)
        => _shares = StateFactory.NewKvasStored<ActiveShare[]>(
            new (LocalSettings, nameof(ActiveShare)) {
                InitialValue = [],
                Corrector = DropExpired,
                Category = StateCategories.Get(GetType(), nameof(_shares)),
            });

    public Task StartSharing(ChatId chatId, TimeSpan duration, CancellationToken cancellationToken)
    {
        var share = new ActiveShare(chatId, ServerNow + duration);
        lock (Lock)
            _shares.Value = _shares.Value.Where(x => x.ChatId != chatId).Append(share).ToArray();
        return Task.CompletedTask;
    }

    public async Task StopSharing(ChatId chatId, CancellationToken cancellationToken)
    {
        lock (Lock)
            _shares.Value = _shares.Value.Where(x => x.ChatId != chatId).ToArray();
        await Commander.Call(new LiveLocations_Stop(Session, chatId), cancellationToken).ConfigureAwait(false);
    }

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var changes = _shares.Computed.Changes(cancellationToken);
        FuncWorker? worker = null;
        await foreach (var cShares in changes.ConfigureAwait(false)) {
            await worker.DisposeSilentlyAsync().ConfigureAwait(false);
            // TODO: stop tracking/sharing if no shares
            worker = FuncWorker.Start(ct => ReportLoop(cShares.Value, ct), cancellationToken);
        }
    }

    // Private methods

    private async Task ReportLoop(ActiveShare[] activeShares, CancellationToken cancellationToken)
    {
        using var cts = cancellationToken.CreateLinkedTokenSource(activeShares.Max(x => x.ExpiresAt) - ServerNow);
        await AsyncChain.From(ReportForChats)
            .Log(LogLevel.Debug, Log)
            .RetryForever(RetryDelaySeq.Exp(0.5, 10), Log)
            .AppendDelay(Constants.LiveLocation.UpdatePeriod)
            .CycleForever()
            .RunIsolated(cts.Token)
            .ConfigureAwait(false);
        return;

        Task ReportForChats(CancellationToken cancellationToken1)
            => activeShares.Select(Report).Collect(cancellationToken1);

        async Task Report(ActiveShare share)
        {
            // TODO: duration should not be reported every time
            var duration = share.ExpiresAt - ServerNow;
            if (duration < TimeSpan.Zero)
                return;

            var point = await Tracker.LastKnown.Use(cancellationToken).ConfigureAwait(false);
            if (point is null)
                return;

            await Commander.Call(new LiveLocations_Report(Session, share.ChatId, point, duration), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private ValueTask<ActiveShare[]> DropExpired(ActiveShare[] shares, CancellationToken cancellationToken)
        => new (shares.Where(x => x.ExpiresAt > ServerNow).ToArray());
}
