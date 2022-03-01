namespace ActualChat.Chat.UI.Blazor.Services;

/// <summary> Must be scoped service. </summary>
public sealed class ChatController : IAsyncDisposable
{
    private readonly ConcurrentDictionary<Symbol, Lazy<ChatPlayer>> _players = new();
    private readonly IChatPlayerFactory _factory;
    private readonly ILogger<ChatController> _log;
    private int _isDisposed;

    public ChatController(ILogger<ChatController> log, IChatPlayerFactory factory)
    {
        _log = log;
        _factory = factory;
    }

    public ChatPlayer GetPlayer(Symbol chatId)
    {
        if (_isDisposed == 1)
            throw new ObjectDisposedException(nameof(ChatController));
        return _players.GetOrAdd(
            chatId,
            static (key, self) => new Lazy<ChatPlayer>(() => self._factory.Create(key)),
            this
        ).Value;
    }

    /// <summary> Disposes all resources allocated for <paramref name="chatId"/> </summary>
    public async ValueTask Close(Symbol chatId)
    {
        if (_isDisposed == 1)
            throw new ObjectDisposedException(nameof(ChatController));
        if (_players.TryRemove(chatId, out var player)) {
            await player.Value.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _isDisposed, 1, 0) != 0)
            return;

        GC.SuppressFinalize(this);

        var playerDisposeTasks = _players
            .Select(kv => DisposePlayer(kv.Key, kv.Value.Value))
            .ToArray();

        _players.Clear();
        if (playerDisposeTasks.Length > 0)
            await Task.WhenAll(playerDisposeTasks).ConfigureAwait(false);

        async Task DisposePlayer(Symbol chatId, ChatPlayer player)
        {
            try {
                await player.DisposeAsync().ConfigureAwait(true);
            }
            catch (Exception e) {
                _log.LogError(e, "Can't dispose player for chatId:{chatId}", chatId);
            }
        }
    }
}