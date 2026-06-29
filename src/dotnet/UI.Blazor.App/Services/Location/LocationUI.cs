using ActualChat.Kvas;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Drives the local user's live-location shares: while at least one chat is being shared it runs the
/// platform <see cref="ILocationTracker"/> and, once per <see cref="Constants.Location.UpdatePeriod"/>,
/// reports the current position to each share's <see cref="SharedLocationId"/>. The first fix of a new
/// share posts a chat entry (minting the id); later fixes update only the shared-location record.
/// Shares are persisted locally and resumed on restart.
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
        var share = new ActiveShare(chatId, null, ServerNow + duration);
        lock (Lock)
            _shares.Value = _shares.Value.Where(x => x.ChatId != chatId).Append(share).ToArray();
        return Task.CompletedTask;
    }

    public async Task SendCurrentLocation(ChatId chatId, CancellationToken cancellationToken)
    {
        if (await Tracker.Get(cancellationToken).ConfigureAwait(false) is not { } point)
            return;

        var locationId = SharedLocationId.New();
        await Commander.Call(
                new SharedLocations_Create(Session, chatId, locationId, point, TimeSpan.Zero),
                cancellationToken)
            .ConfigureAwait(false);
        var command = new Chats_UpsertEntry(Session, chatId, null) { LocationId = locationId };
        await Commander.Call(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task StopSharing(ChatId chatId, CancellationToken cancellationToken)
    {
        // Start/stop is device-local: only the device that started a share can stop it,
        // using the SharedLocationId it persisted in _shares.
        ActiveShare[] stopped;
        lock (Lock) {
            stopped = _shares.Value.Where(x => x.ChatId == chatId).ToArray();
            _shares.Value = _shares.Value.Where(x => x.ChatId != chatId).ToArray();
        }
        foreach (var share in stopped) {
            if (share.LocationId is not { } locationId)
                continue;

            await Commander.Call(new SharedLocations_Stop(Session, chatId, locationId), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var changes = _shares.Computed.Changes(cancellationToken);
        FuncWorker? worker = null;
        await foreach (var cShares in changes.ConfigureAwait(false)) {
            await worker.DisposeSilentlyAsync().ConfigureAwait(false);
            if (cShares.Value.Length == 0) {
                worker = null;
                await Tracker.Stop(cancellationToken).ConfigureAwait(false);
                continue;
            }
            worker = FuncWorker.Start(ct => ReportLoop(cShares.Value, ct), cancellationToken);
        }
    }

    // Private methods

    private async Task ReportLoop(ActiveShare[] activeShares, CancellationToken cancellationToken)
    {
        await Tracker.Start(cancellationToken).ConfigureAwait(false);
        using var cts = cancellationToken.CreateLinkedTokenSource(activeShares.Max(x => x.ExpiresAt) - ServerNow);
        await AsyncChain.From(ReportForChats)
            .Log(LogLevel.Debug, Log)
            .RetryForever(RetryDelaySeq.Exp(0.5, 10), Log)
            .AppendDelay(Constants.Location.UpdatePeriod)
            .CycleForever()
            .RunIsolated(cts.Token)
            .ConfigureAwait(false);
        return;

        Task ReportForChats(CancellationToken cancellationToken1)
            => activeShares.Select(Report).Collect(cancellationToken1);

        async Task Report(ActiveShare share)
        {
            if (share.ExpiresAt <= ServerNow)
                return;

            var point = await Tracker.LastKnown.Use(cancellationToken).ConfigureAwait(false);
            if (point is null)
                return;

            if (share.LocationId is not { } locationId) {
                await PostEntry(share, point).ConfigureAwait(false);
                return;
            }

            await Commander.Call(new SharedLocations_Report(Session, share.ChatId, locationId, point), cancellationToken)
                .ConfigureAwait(false);
        }

        async Task PostEntry(ActiveShare share, GeoPoint point)
        {
            var liveDuration = share.ExpiresAt - ServerNow;
            var locationId = SharedLocationId.New();
            await Commander.Call(
                    new SharedLocations_Create(Session, share.ChatId, locationId, point, liveDuration),
                    cancellationToken)
                .ConfigureAwait(false);
            var command = new Chats_UpsertEntry(Session, share.ChatId, null) { LocationId = locationId };
            await Commander.Call(command, cancellationToken).ConfigureAwait(false);

            lock (Lock)
                _shares.Value = _shares.Value
                    .Select(x => x.ChatId == share.ChatId && x.LocationId is null
                        ? x with { LocationId = locationId }
                        : x)
                    .ToArray();
        }
    }

    private ValueTask<ActiveShare[]> DropExpired(ActiveShare[] shares, CancellationToken cancellationToken)
        => new (shares.Where(x => x.ExpiresAt > ServerNow).ToArray());
}
