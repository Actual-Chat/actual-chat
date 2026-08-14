using ActualChat.Kvas;
using ActualChat.Users;
using ActualLab.Interception;

namespace ActualChat.UI.Blazor.App.Services;

public class NotificationsPanelUI : UIWorkerBase<AppUIHub>, IComputeService, INotifyInitialized
{
    private static readonly ChatListFilter[] Filters =
        [ChatListFilter.Unread, ChatListFilter.UnreadDMs, ChatListFilter.UnreadMentions];
    private static readonly TimeSpan GcPeriod = TimeSpan.FromSeconds(60);
    // Storage upper bound: entries older than this can never be shown under any retention setting.
    private static readonly TimeSpan MaxRetention = NotificationsPanelRetention.Hours24.ToTimeSpan();

    private readonly object _lock = new();
    private readonly StoredState<RetainedRead[]> _reads;
    private readonly MutableState<int> _tick;
    private readonly MutableState<Moment?> _panelOpenedAt;

    private ChatListUI ChatListUI => Hub.ChatListUI;
    private Moment Now => Clocks.SystemClock.Now;

    public NotificationsPanelUI(AppUIHub hub) : base(hub)
    {
        _reads = StateFactory.NewKvasStored<RetainedRead[]>(
            new(LocalSettings, nameof(NotificationsPanelUI)) {
                InitialValue = [],
                Category = StateCategories.Get(GetType(), nameof(_reads)),
            });
        _tick = StateFactory.NewMutable(0, StateCategories.Get(GetType(), nameof(_tick)));
        _panelOpenedAt = StateFactory.NewMutable(
            (Moment?)null, StateCategories.Get(GetType(), nameof(_panelOpenedAt)));
    }

    void INotifyInitialized.Initialized()
        => this.Start();

    // Panel open/close - only matters for the UntilPanelClosed retention mode.
    public void Open()
    {
        if (_panelOpenedAt.Value == null)
            _panelOpenedAt.Value = Now;
    }

    public void Close()
        => _panelOpenedAt.Value = null;

    // Read chats still retained for the given panel filter under the current retention setting.
    [ComputeMethod]
    public virtual async Task<ImmutableHashSet<ChatId>> GetRetainedSet(
        Symbol filterId, CancellationToken cancellationToken = default)
    {
        _ = await _tick.Use(cancellationToken).ConfigureAwait(false);
        var reads = await _reads.Use(cancellationToken).ConfigureAwait(false);
        if (reads.Length == 0)
            return ImmutableHashSet<ChatId>.Empty;

        var settings = await UserSettingsUI.UserNotificationsPanelSettings()
            .Get(cancellationToken).ConfigureAwait(false);
        var retention = settings.Retention;
        if (retention == NotificationsPanelRetention.Immediately)
            return ImmutableHashSet<ChatId>.Empty;

        var filterKey = filterId.Value;
        var result = ImmutableHashSet.CreateBuilder<ChatId>();
        if (retention == NotificationsPanelRetention.UntilPanelClosed) {
            var openedAt = await _panelOpenedAt.Use(cancellationToken).ConfigureAwait(false);
            if (openedAt is not { } since)
                return ImmutableHashSet<ChatId>.Empty;
            foreach (var read in reads)
                if (read.FilterId == filterKey && read.ReadAt >= since)
                    result.Add(read.ChatId);
            return result.ToImmutable();
        }

        var duration = retention.ToTimeSpan();
        if (duration <= TimeSpan.Zero)
            return ImmutableHashSet<ChatId>.Empty;

        var now = Now;
        foreach (var read in reads)
            if (read.FilterId == filterKey && now - read.ReadAt < duration)
                result.Add(read.ChatId);
        return result.ToImmutable();
    }

    // Private methods

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        await _reads.WhenRead.ConfigureAwait(false);
        foreach (var filter in Filters)
            _ = Track(filter, cancellationToken);

        while (!cancellationToken.IsCancellationRequested) {
            CollectGarbage();
            // Re-publish so a time-based window is re-evaluated even when nothing was read/expired.
            // Immediately / UntilPanelClosed don't depend on wall-clock, so they need no periodic tick.
            if (_reads.Value.Length > 0 && await IsTimeBased(cancellationToken).ConfigureAwait(false))
                _tick.Value++;
            await Task.Delay(GcPeriod, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task Track(ChatListFilter filter, CancellationToken cancellationToken)
    {
        try {
            HashSet<ChatId>? previous = null;
            var computed = await Computed
                .Capture(() => ChatListUI.ListUnordered(null, filter, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            await foreach (var c in computed.Changes(cancellationToken).ConfigureAwait(false)) {
                if (c.HasError)
                    continue;
                var current = c.Value.Keys.ToHashSet();
                OnUnreadChanged(filter.Id.Value, previous, current);
                previous = current;
            }
        }
        catch (OperationCanceledException) {
            // Worker stopped
        }
    }

    private async Task<bool> IsTimeBased(CancellationToken cancellationToken)
    {
        var settings = await UserSettingsUI.UserNotificationsPanelSettings()
            .Get(cancellationToken).ConfigureAwait(false);
        return settings.Retention
            is not (NotificationsPanelRetention.Immediately or NotificationsPanelRetention.UntilPanelClosed);
    }

    private void OnUnreadChanged(string filterId, HashSet<ChatId>? previous, HashSet<ChatId> current)
    {
        lock (_lock) {
            var list = _reads.Value.ToList();
            var changed = false;
            // A chat that was unread and no longer is has just been read - start retaining it.
            if (previous != null)
                foreach (var chatId in previous)
                    if (!current.Contains(chatId)
                        && !list.Any(r => r.FilterId == filterId && r.ChatId == chatId)) {
                        list.Add(new RetainedRead(filterId, chatId, Now));
                        changed = true;
                    }
            // A chat that's unread again must not be retained - it shows via the live list instead.
            changed |= list.RemoveAll(r => r.FilterId == filterId && current.Contains(r.ChatId)) > 0;
            if (changed)
                _reads.Value = list.ToArray();
        }
    }

    private void CollectGarbage()
    {
        lock (_lock) {
            var reads = _reads.Value;
            if (reads.Length == 0)
                return;

            var now = Now;
            var kept = reads.Where(r => now - r.ReadAt < MaxRetention).ToArray();
            if (kept.Length != reads.Length)
                _reads.Value = kept;
        }
    }
}

[DataContract, MessagePackObject]
public sealed partial record RetainedRead(
    [property: DataMember, Key(0)] string FilterId,
    [property: DataMember, Key(1)] ChatId ChatId,
    [property: DataMember, Key(2)] Moment ReadAt);
