using ActualChat.MediaPlayback;

namespace ActualChat.Chat.UI.Blazor.Services;

public enum ChatPlayerKind { Realtime, Historical }

public abstract class ChatPlayer : ProcessorBase
{
    /// <summary>
    /// Once enqueued, playback loop continues, so the larger is this duration,
    /// the higher is the chance to enqueue the next entry on time.
    /// </summary>
    protected static readonly TimeSpan EnqueueAheadDuration = TimeSpan.FromSeconds(1);
    protected static readonly TimeSpan InfDuration = 2 * Constants.Chat.MaxEntryDuration;
    protected static readonly TimeSpan MaxPlayStartTime = TimeSpan.FromSeconds(3);

    private volatile CancellationTokenSource? _playTokenSource;
    private volatile Task? _whenPlaying = null;

    protected ILogger Log { get; }
    protected MomentClockSet Clocks { get; }
    protected IServiceProvider Services { get; }
    protected IAuthors Authors { get; }
    protected IChats Chats { get; }

    public Session Session { get; }
    public Symbol ChatId { get; }
    public ChatPlayerKind PlayerKind { get; protected init; }
    public Playback Playback { get; }
    public Task? WhenPlaying => _whenPlaying;

    protected ChatPlayer(Session session, Symbol chatId, IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());
        Clocks = services.Clocks();

        ChatId = chatId;
        Session = session;

        Playback = services.GetRequiredService<IPlaybackFactory>().Create();
        Authors = services.GetRequiredService<IAuthors>();
        Chats = services.GetRequiredService<IChats>();
    }

    protected override async Task DisposeAsyncCore()
    {
        try {
            await Stop().ConfigureAwait(false);
        }
        catch {
            // Intended
        }
        await Playback.DisposeAsync().ConfigureAwait(false);
    }

    public async Task<Task> Start(Moment startAt, CancellationToken cancellationToken)
    {
        this.ThrowIfDisposedOrDisposing();
        CancellationTokenSource playTokenSource;
        CancellationToken playToken;

        var spinWait = new SpinWait();
        var whenPlayingSource = TaskSource.New<Unit>(true);
        while (true) {
            await Stop().ConfigureAwait(false);
            lock (Lock)
                if (_playTokenSource == null) {
                    _playTokenSource = playTokenSource = cancellationToken.LinkWith(StopToken);
                    _whenPlaying = whenPlayingSource.Task;
                    playToken = playTokenSource.Token;
                    break;
                }
            // Some other Play has already started in between Stop() & lock (Lock)
            spinWait.SpinOnce();
        }

        _ = BackgroundTask.Run(async () => {
            var chatEntryPlayer = new ChatEntryPlayer(Session, ChatId, Playback, Services, playToken);
            try {
                await Play(chatEntryPlayer, startAt, playToken).ConfigureAwait(false);
            }
            catch (Exception e) {
                if (e is not OperationCanceledException)
                    Log.LogError(e, "Playback (reader part) failed in chat #{ChatId}", ChatId);
                chatEntryPlayer.Abort();
            }
            finally {
                // We should wait for playback completion first
                await chatEntryPlayer.DisposeAsync().ConfigureAwait(false);
                playTokenSource.CancelAndDisposeSilently();
                whenPlayingSource.TrySetResult(default);
                lock (Lock)
                    if (_playTokenSource == playTokenSource) {
                        _playTokenSource = null;
                        _whenPlaying = null;
                    }
            }
        }, CancellationToken.None);

        return whenPlayingSource.Task;
    }

    public Task Stop()
    {
        CancellationTokenSource? playTokenSource;
        Task? whenPlaying;
        lock (Lock) {
            playTokenSource = _playTokenSource;
            whenPlaying = _whenPlaying;
        }
        playTokenSource?.CancelAndDisposeSilently();
        return whenPlaying ?? Task.CompletedTask;
    }

    // Protected methods

    protected abstract Task Play(
        ChatEntryPlayer entryPlayer, Moment startAt, CancellationToken cancellationToken);
}
