using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Stl.Fusion.Blazor;

namespace ActualChat.Chat.UI.Blazor.Services;

// This service can be used only from the UI thread
public class ChatMediaPlayers : IAsyncDisposable
{
    private Dictionary<ChatId, ChatMediaPlayer> RealtimePlayers { get; } = new();
    private Dictionary<ChatId, ChatMediaPlayer> HistoricalPlayers { get; } = new();

    private ILogger Log { get; }
    private IServiceProvider Services { get; }
    private BlazorCircuitContext CircuitContext { get; }
    private Session Session { get; }
    private IChats Chats { get; }
    private IChatAuthors ChatAuthors { get; }
    private AuthStateProvider AuthStateProvider { get; }

    public ChatMediaPlayers(IServiceProvider services)
    {
        Log = services.LogFor(GetType());
        Services = services;
        CircuitContext = Services.GetRequiredService<BlazorCircuitContext>();
        Session = Services.GetRequiredService<Session>();
        Chats = Services.GetRequiredService<IChats>();
        ChatAuthors = Services.GetRequiredService<IChatAuthors>();
        AuthStateProvider = Services.GetRequiredService<AuthStateProvider>();
        AuthStateProvider.AuthenticationStateChanged += OnAuthStateChanged;
    }

    public ValueTask DisposeAsync()
        => Reset();

    public async ValueTask Reset()
    {
        var players = RealtimePlayers.Values.Concat(HistoricalPlayers.Values).ToList();
        RealtimePlayers.Clear();
        HistoricalPlayers.Clear();
        foreach (var player in players.OrderBy(p => p.ChatId.Value)) {
            try {
                await player.DisposeAsync().ConfigureAwait(true);
            }
            catch (Exception e) {
                Log.LogError(e, "MediaPlayer.DisposeAsync() failed");
            }
        }
    }

    public ValueTask<ChatMediaPlayer> GetRealtimePlayer(
        ChatId chatId, CancellationToken cancellationToken = default)
    {
        var player = RealtimePlayers.GetValueOrDefault(chatId);
        if (player is { IsDisposed: false })
            return ValueTask.FromResult(player);

        player = new ChatMediaPlayer(Services) {
            IsRealTimePlayer = true,
            ChatId = chatId,
            Session = Session,
        };
        RealtimePlayers[chatId] = player;
        return ValueTask.FromResult(player);
    }

    public ValueTask<ChatMediaPlayer> GetHistoricalPlayer(
        ChatId chatId, CancellationToken cancellationToken = default)
    {
        var player = HistoricalPlayers.GetValueOrDefault(chatId);
        if (player is { IsDisposed: false })
            return ValueTask.FromResult(player);

        player = new ChatMediaPlayer(Services) {
            IsRealTimePlayer = false,
            ChatId = chatId,
            Session = Session,
        };
        HistoricalPlayers[chatId] = player;
        return ValueTask.FromResult(player);
    }

    public async ValueTask DisposePlayers(ChatId chatId)
    {
        var player = RealtimePlayers.GetValueOrDefault(chatId);
        if (player != null)
            await player.DisposeAsync();
        player = HistoricalPlayers.GetValueOrDefault(chatId);
        if (player != null)
            await player.DisposeAsync();
    }

    private void OnAuthStateChanged(Task<AuthenticationState> task)
        => CircuitContext.Dispatcher.InvokeAsync(() => Reset().AsTask());
}
