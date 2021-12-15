namespace ActualChat.Chat.UI.Blazor.Services;

// This service can be used only from the UI thread
public class ChatPlayers : IAsyncDisposable
{
    private ILogger? _log;

    private Dictionary<Symbol, ChatPlayer> RealtimePlayers { get; } = new();
    private Dictionary<Symbol, ChatPlayer> HistoricalPlayers { get; } = new();

    private ILogger Log => _log ??= Services.LogFor(GetType());
    private IServiceProvider Services { get; }
    private BlazorCircuitContext CircuitContext { get; }
    private Session Session { get; }

    public ChatPlayers(IServiceProvider services)
    {
        Services = services;
        CircuitContext = Services.GetRequiredService<BlazorCircuitContext>();
        Session = Services.GetRequiredService<Session>();
    }

    public async ValueTask DisposeAsync()
    {
        var players = RealtimePlayers.Values.Concat(HistoricalPlayers.Values).ToList();
        RealtimePlayers.Clear();
        HistoricalPlayers.Clear();
        foreach (var player in players.OrderBy(p => p.ChatId)) {
            try {
                await player.DisposeAsync().ConfigureAwait(true);
            }
            catch (Exception e) {
                Log.LogError(e, "ChatPlayer.DisposeAsync() failed");
            }
        }
    }

    public ValueTask<ChatPlayer> GetRealtimePlayer(
        Symbol chatId, CancellationToken cancellationToken = default)
    {
        var player = RealtimePlayers.GetValueOrDefault(chatId);
        if (player is { IsDisposeStarted: false })
            return ValueTask.FromResult(player);

        player = new ChatPlayer(Services) {
            IsRealTimePlayer = true,
            ChatId = chatId,
            Session = Session,
        };
        RealtimePlayers[chatId] = player;
        return ValueTask.FromResult(player);
    }

    public ValueTask<ChatPlayer> GetHistoricalPlayer(
        Symbol chatId, CancellationToken cancellationToken = default)
    {
        var player = HistoricalPlayers.GetValueOrDefault(chatId);
        if (player is { IsDisposeStarted: false })
            return ValueTask.FromResult(player);

        player = new ChatPlayer(Services) {
            IsRealTimePlayer = false,
            ChatId = chatId,
            Session = Session,
        };
        HistoricalPlayers[chatId] = player;
        return ValueTask.FromResult(player);
    }

    public async ValueTask DisposePlayers(string chatId)
    {
        var player = RealtimePlayers.GetValueOrDefault(chatId);
        if (player != null)
            await player.DisposeAsync().ConfigureAwait(false);
        player = HistoricalPlayers.GetValueOrDefault(chatId);
        if (player != null)
            await player.DisposeAsync().ConfigureAwait(false);
    }
}
