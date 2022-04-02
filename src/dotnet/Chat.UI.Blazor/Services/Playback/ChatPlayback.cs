using ActualChat.MediaPlayback;

namespace ActualChat.Chat.UI.Blazor.Services;

// ReSharper disable once ClassNeverInstantiated.Global
public class ChatPlayback : SafeAsyncDisposableBase
{
    private readonly ConcurrentDictionary<Symbol, ChatPlayer> _players = new();
    private readonly IServiceProvider _services;
    private readonly ChatPlaybackState _state;
    private readonly MomentClockSet _clocks;
    private readonly ILogger _log;

    public IReadOnlyDictionary<Symbol, ChatPlayer> Players => _players;

    public ChatPlayback(IServiceProvider services)
    {
        _services = services;
        _log = services.LogFor(GetType());
        _clocks = services.Clocks();
        _state = services.GetRequiredService<ChatPlaybackState>();
    }

    protected override async Task DisposeAsync(bool disposing)
    {
        while (_players.Count != 0) {
            var playerIds = _players.Keys.ToArray();
            await Task.WhenAll(playerIds.Select(ClosePlayer)).ConfigureAwait(false);
        }
    }

    [ComputeMethod]
    public virtual Task<ChatPlayer?> GetPlayer(Symbol chatId, CancellationToken cancellationToken)
        => Task.FromResult(_players.GetValueOrDefault(chatId));

    public ChatPlayer ActivatePlayer(Symbol chatId)
    {
        this.ThrowIfDisposedOrDisposing();
        var player = _players.GetOrAdd(chatId,
            static (key, self) => self._services.Activate<ChatPlayer>(key),
            this);
        using (Computed.Invalidate())
            _ = GetPlayer(chatId, default);
        return player;
    }


    public async Task ClosePlayer(Symbol chatId)
    {
        if (!_players.TryRemove(chatId, out var player))
            return;

        await player.DisposeAsync().ConfigureAwait(true);
        using (Computed.Invalidate())
            _ = GetPlayer(chatId, default);
    }

    // Playback control

    public async Task Stop(Symbol chatId, CancellationToken cancellationToken)
    {
        var computed = await Computed.Capture(ct => _state.GetMode(chatId, ct), cancellationToken)
            .ConfigureAwait(false);
        _ = Players.GetValueOrDefault(chatId)?.Stop();
        await computed.When(m => m is ChatPlaybackMode.None or ChatPlaybackMode.RealtimeMuted, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task Stop(Symbol chatId, PlaybackKind playbackKind, CancellationToken cancellationToken)
    {
        var player = Players.GetValueOrDefault(chatId);
        return player == null || player.PlaybackKindState.ValueOrDefault != playbackKind
            ? Task.CompletedTask
            : Stop(chatId, cancellationToken);
    }

    public async Task StartRealtime(Symbol chatId, CancellationToken cancellationToken)
    {
        var computed = await Computed.Capture(ct => _state.GetMode(chatId, ct), cancellationToken)
            .ConfigureAwait(false);
        if (computed.ValueOrDefault is ChatPlaybackMode.Realtime)
            return;

        var player = ActivatePlayer(chatId);
        _ = BackgroundTask.Run(async () => {
            await player.Stop().ConfigureAwait(false);
            try {
                var playTask = player.Play(_clocks.SystemClock.Now, isRealtime: true, CancellationToken.None);
                _state.SetMode(chatId, ChatPlaybackMode.Realtime);
                await playTask.ConfigureAwait(false);
            }
            finally {
                if (_state[chatId] == ChatPlaybackMode.Realtime)
                    _state.SetMode(chatId, ChatPlaybackMode.None);
            }
        }, _log, $"Realtime playback failed for chat #{chatId}", CancellationToken.None);
        await computed.When(m => m is ChatPlaybackMode.Realtime, cancellationToken).ConfigureAwait(false);
    }

    public async Task StartHistorical(Symbol chatId, Moment startAt, CancellationToken cancellationToken)
    {
        var computed = await Computed.Capture(ct => _state.GetMode(chatId, ct), cancellationToken)
            .ConfigureAwait(false);
        var player = ActivatePlayer(chatId);
        _ = BackgroundTask.Run(async () => {
            await player.Stop().ConfigureAwait(false);
            try {
                var playTask = player.Play(startAt, isRealtime: false, CancellationToken.None);
                _state.SetMode(chatId, ChatPlaybackMode.Historical);
                await playTask.ConfigureAwait(false);
            }
            finally {
                if (_state[chatId] == ChatPlaybackMode.Historical)
                    _state.SetMode(chatId, ChatPlaybackMode.None);
            }
        }, _log, $"Realtime playback failed for chat #{chatId}", CancellationToken.None);
        await computed.When(m => m is ChatPlaybackMode.Historical, cancellationToken).ConfigureAwait(false);
    }

    public async Task MuteRealtime(Symbol chatId, CancellationToken cancellationToken)
    {
        await Stop(chatId, cancellationToken).ConfigureAwait(false);
        _state.SetMode(chatId, ChatPlaybackMode.RealtimeMuted);
    }

    public async Task<bool> UnmuteRealtime(Symbol chatId, CancellationToken cancellationToken)
    {
        if (_state[chatId] != ChatPlaybackMode.RealtimeMuted)
            return false;
        await StartRealtime(chatId, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task StartRealtimeAndMuteOther(Symbol chatId, CancellationToken cancellationToken)
    {
        var infos = _state.List;
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
}
