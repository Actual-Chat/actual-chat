using ActualChat.MediaPlayback;

namespace ActualChat.Chat.UI.Blazor.Services;

/// <summary> Must be scoped service. </summary>
public class ChatPlayers : SafeAsyncDisposableBase
{
    private readonly ConcurrentDictionary<Symbol, ChatPlayer> _players = new();
    private readonly IServiceProvider _services;
    private readonly ChatPlaybackInfos _chatPlaybackInfos;
    private readonly MomentClockSet _clocks;
    private readonly ILogger _log;

    public ChatPlayer? this[Symbol chatId] // Won't create dependency
        => _players.GetValueOrDefault(chatId);

    public ChatPlayers(IServiceProvider services)
    {
        _services = services;
        _log = services.LogFor(GetType());
        _clocks = services.Clocks();
        _chatPlaybackInfos = services.GetRequiredService<ChatPlaybackInfos>();
    }

    protected override async Task DisposeAsync(bool disposing)
    {
        while (_players.Count != 0) {
            var playerIds = _players.Keys.ToArray();
            await Task.WhenAll(playerIds.Select(Close)).ConfigureAwait(false);
        }
    }

    [ComputeMethod]
    public virtual Task<ChatPlayer?> Get(Symbol chatId, CancellationToken cancellationToken)
        => Task.FromResult(_players.GetValueOrDefault(chatId));

    public ChatPlayer Activate(Symbol chatId)
    {
        this.ThrowIfDisposedOrDisposing();
        var player = _players.GetOrAdd(chatId,
            static (key, self) => self._services.Activate<ChatPlayer>(key),
            this);
        using (Computed.Invalidate())
            _ = Get(chatId, default);
        return player;
    }

    public async Task Stop(Symbol chatId, CancellationToken cancellationToken)
    {
        var computed = await Computed.Capture(ct => _chatPlaybackInfos.GetMode(chatId, ct), cancellationToken)
            .ConfigureAwait(false);
        _ = this[chatId]?.Stop();
        await computed.When(m => m is ChatPlaybackMode.None or ChatPlaybackMode.RealtimeMuted, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task Stop(Symbol chatId, PlaybackKind playbackKind, CancellationToken cancellationToken)
    {
        var player = this[chatId];
        return player == null || player.PlaybackKindState.ValueOrDefault != playbackKind
            ? Task.CompletedTask
            : Stop(chatId, cancellationToken);
    }

    public async Task StartRealtime(Symbol chatId, CancellationToken cancellationToken)
    {
        var computed = await Computed.Capture(ct => _chatPlaybackInfos.GetMode(chatId, ct), cancellationToken)
            .ConfigureAwait(false);
        if (computed.ValueOrDefault is ChatPlaybackMode.Realtime)
            return;

        var player = Activate(chatId);
        _ = BackgroundTask.Run(async () => {
            await player.Stop().ConfigureAwait(false);
            try {
                var playTask = player.Play(_clocks.SystemClock.Now, isRealtime: true, CancellationToken.None);
                _chatPlaybackInfos.SetMode(chatId, ChatPlaybackMode.Realtime);
                await playTask.ConfigureAwait(false);
            }
            finally {
                if (_chatPlaybackInfos[chatId] == ChatPlaybackMode.Realtime)
                    _chatPlaybackInfos.SetMode(chatId, ChatPlaybackMode.None);
            }
        }, _log, $"Realtime playback failed for chat #{chatId}", CancellationToken.None);
        await computed.When(m => m is ChatPlaybackMode.Realtime, cancellationToken).ConfigureAwait(false);
    }

    public async Task StartHistorical(Symbol chatId, Moment startAt, CancellationToken cancellationToken)
    {
        var computed = await Computed.Capture(ct => _chatPlaybackInfos.GetMode(chatId, ct), cancellationToken)
            .ConfigureAwait(false);
        var player = Activate(chatId);
        _ = BackgroundTask.Run(async () => {
            await player.Stop().ConfigureAwait(false);
            try {
                var playTask = player.Play(startAt, isRealtime: false, CancellationToken.None);
                _chatPlaybackInfos.SetMode(chatId, ChatPlaybackMode.Historical);
                await playTask.ConfigureAwait(false);
            }
            finally {
                if (_chatPlaybackInfos[chatId] == ChatPlaybackMode.Historical)
                    _chatPlaybackInfos.SetMode(chatId, ChatPlaybackMode.None);
            }
        }, _log, $"Realtime playback failed for chat #{chatId}", CancellationToken.None);
        await computed.When(m => m is ChatPlaybackMode.Historical, cancellationToken).ConfigureAwait(false);
    }

    public async Task MuteRealtime(Symbol chatId, CancellationToken cancellationToken)
    {
        await Stop(chatId, cancellationToken).ConfigureAwait(false);
        _chatPlaybackInfos.SetMode(chatId, ChatPlaybackMode.RealtimeMuted);
    }

    public async Task<bool> UnmuteRealtime(Symbol chatId, CancellationToken cancellationToken)
    {
        if (_chatPlaybackInfos[chatId] != ChatPlaybackMode.RealtimeMuted)
            return false;
        await StartRealtime(chatId, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task StartRealtimeAndMuteOther(Symbol chatId, CancellationToken cancellationToken)
    {
        var infos = _chatPlaybackInfos.List;
        var tasks = new List<Task>();
        foreach (var info in infos) {
            var task = info.ChatId == chatId
                ? (info.Mode != ChatPlaybackMode.Realtime ? StartRealtime(chatId, cancellationToken) : Task.CompletedTask)
                : (info.Mode != ChatPlaybackMode.RealtimeMuted ? MuteRealtime(chatId, cancellationToken) : Task.CompletedTask);
            if (!task.IsCompletedSuccessfully)
                tasks.Add(task);
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    public async Task Close(Symbol chatId)
    {
        if (!_players.TryRemove(chatId, out var player))
            return;

        await player.DisposeAsync().ConfigureAwait(true);
        using (Computed.Invalidate())
            _ = Get(chatId, default);
    }
}
