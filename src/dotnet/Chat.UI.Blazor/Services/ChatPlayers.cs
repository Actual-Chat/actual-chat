namespace ActualChat.Chat.UI.Blazor.Services;

// This service can be used only from the UI thread
public class ChatPlayers : IAsyncDisposable
{
    private ILogger? _log;

    private Dictionary<Symbol, RealtimeChatPlayer> RealtimePlayers { get; } = new();
    private Dictionary<Symbol, HistoricalChatPlayer> HistoricalPlayers { get; } = new();

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
        var players = RealtimePlayers.Values.Cast<ChatPlayer>()
            .Concat(HistoricalPlayers.Values).ToList();
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

    public ValueTask<RealtimeChatPlayer> GetRealtimePlayer(
        Symbol chatId, CancellationToken cancellationToken = default)
    {
        var player = RealtimePlayers.GetValueOrDefault(chatId);
        if (player is { IsDisposeStarted: false })
            return ValueTask.FromResult(player);

        player = new RealtimeChatPlayer(Services) {
            ChatId = chatId,
            Session = Session,
        };
        RealtimePlayers[chatId] = player;
        return ValueTask.FromResult(player);
    }

    public ValueTask<HistoricalChatPlayer> GetHistoricalPlayer(
        Symbol chatId, CancellationToken cancellationToken = default)
    {
        var player = HistoricalPlayers.GetValueOrDefault(chatId);
        if (player is { IsDisposeStarted: false })
            return ValueTask.FromResult(player);

        player = new HistoricalChatPlayer(Services) {
            ChatId = chatId,
            Session = Session,
        };
        HistoricalPlayers[chatId] = player;
        return ValueTask.FromResult(player);
    }

    public async ValueTask DisposePlayers(string chatId)
    {
        var chatPlayer = (ChatPlayer?) RealtimePlayers.GetValueOrDefault(chatId);
        if (chatPlayer != null)
            await chatPlayer.DisposeAsync().ConfigureAwait(false);
        chatPlayer = HistoricalPlayers.GetValueOrDefault(chatId);
        if (chatPlayer != null)
            await chatPlayer.DisposeAsync().ConfigureAwait(false);
    }
}
