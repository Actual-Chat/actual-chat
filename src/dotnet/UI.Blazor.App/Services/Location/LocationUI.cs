using ActualChat.Kvas;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// The local user's live-location shares: the device-local list of chats being shared (persisted and
/// resumed on restart) plus the start/stop/send-once API. <see cref="LocationReporter"/> observes this
/// state and does the actual position reporting.
/// </summary>
public class LocationUI : UIServiceBase<AppUIHub>, IComputeService
{
    private readonly Lock _lock = new();
    // TODO: try moving to LocationReporter
    private readonly StoredState<ActiveShare[]> _shares;

    private ILocationTracker Tracker => field ??= Hub.Services.GetRequiredService<ILocationTracker>();
    private Moment ServerNow => Clocks.ServerClock.Now;
    internal IState<ActiveShare[]> Shares => _shares;

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
        lock (_lock)
            _shares.Value = _shares.Value.Where(x => x.ChatId != chatId).Append(share).ToArray();
        return Task.CompletedTask;
    }

    public async Task SendCurrentLocation(ChatId chatId, CancellationToken cancellationToken)
    {
        if (await Tracker.Get(cancellationToken).ConfigureAwait(false) is not { } point)
            return;

        var change = Change.Create(new SharedLocationDiff { Point = point, LiveDuration = TimeSpan.Zero });
        var shared = await Commander.Call(
                new SharedLocations_Change(Session, chatId, null, change),
                cancellationToken)
            .ConfigureAwait(false);
        if (shared is null)
            return;

        var command = new Chats_UpsertEntry(Session, chatId, null) { LocationId = shared.Id };
        await Commander.Call(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task StopSharing(ChatId chatId, CancellationToken cancellationToken)
    {
        // Start/stop is device-local: only the device that started a share can stop it,
        // using the SharedLocationId it persisted in _shares.
        ActiveShare[] stopped;
        lock (_lock) {
            stopped = _shares.Value.Where(x => x.ChatId == chatId).ToArray();
            _shares.Value = _shares.Value.Where(x => x.ChatId != chatId).ToArray();
        }
        foreach (var share in stopped) {
            if (share.LocationId is not { } locationId)
                continue;

            var change = Change.Remove<SharedLocationDiff>();
            var stop = new SharedLocations_Change(Session, chatId, locationId, change);
            await Commander.Call(stop, cancellationToken).ConfigureAwait(false);
        }
    }

    // Internal methods

    internal void SetSharedLocationId(ChatId chatId, SharedLocationId locationId)
    {
        lock (_lock)
            _shares.Value = _shares.Value
                .Select(x => x.ChatId == chatId && x.LocationId is null
                    ? x with { LocationId = locationId }
                    : x)
                .ToArray();
    }

    // Private methods

    private ValueTask<ActiveShare[]> DropExpired(ActiveShare[] shares, CancellationToken cancellationToken)
        => new (shares.Where(x => x.ExpiresAt > ServerNow).ToArray());
}
