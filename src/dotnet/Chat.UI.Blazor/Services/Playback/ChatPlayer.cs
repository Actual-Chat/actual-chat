using ActualChat.Hosting;
using ActualChat.MediaPlayback;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Services;

public enum ChatPlayerKind { Realtime, Historical }

public abstract class ChatPlayer : ProcessorBase
{
    /// <summary>
    /// Once enqueued, playback loop continues, so the larger is this duration,
    /// the higher is the chance to enqueue the next entry on time.
    /// </summary>
    protected static readonly TimeSpan EnqueueAheadDuration = TimeSpan.FromSeconds(2);
    protected static readonly TimeSpan MaxEntryDuration = Constants.Chat.MaxEntryDuration + TimeSpan.FromSeconds(5);

    private volatile CancellationTokenSource? _playTokenSource;
    private volatile Task? _whenPlaying = null;

    protected ILogger Log { get; }
    protected ILogger? DebugLog => DebugMode ? Log : null;
    protected static bool DebugMode => Constants.DebugMode.AudioPlayback;

    protected ChatHub ChatHub { get; }
    protected Session Session => ChatHub.Session;
    protected IChats Chats => ChatHub.Chats;
    protected IAuthors Authors => ChatHub.Authors;
    protected InteractiveUI InteractiveUI => ChatHub.InteractiveUI;
    protected MomentClockSet Clocks => ChatHub.Clocks();
    protected HostInfo HostInfo => ChatHub.HostInfo;

    protected IState<TimeSpan> SleepDuration { get; }
    protected IState<TimeSpan> PauseDuration { get; }
    protected TimeSpan SleepAndPauseDuration => SleepDuration.Value + Playback.TotalPauseDuration.Value;

    public ChatId ChatId { get; }
    public ChatPlayerKind PlayerKind { get; protected init; }
    public Playback Playback { get; }
    public string Operation { get; protected set; } = "";
    public Task? WhenPlaying => _whenPlaying;

    protected ChatPlayer(ChatHub chatHub, ChatId chatId)
    {
        ChatHub = chatHub;
        ChatId = chatId;
        Log = ChatHub.LogFor(GetType());

        Playback = ChatHub.PlaybackFactory.Create();
        SleepDuration = ChatHub.DeviceAwakeUI.TotalSleepDuration;
        PauseDuration = Playback.TotalPauseDuration;
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

        var whenPlayingSource = TaskCompletionSourceExt.New<Unit>();
        Task stopTask = Stop();
        while (true) {
            await stopTask.ConfigureAwait(false);
            lock (Lock) {
                if (_playTokenSource == null) {
                    _playTokenSource = playTokenSource = cancellationToken.LinkWith(StopToken);
                    _whenPlaying = whenPlayingSource.Task;
                    playToken = playTokenSource.Token;
                    break;
                }
                stopTask = Stop();
            }
        }

        _ = BackgroundTask.Run(async () => {
            var chatEntryPlayer = new ChatEntryPlayer(ChatHub, ChatId, Playback, playToken);
            try {
                await Play(chatEntryPlayer, startAt, playToken).ConfigureAwait(false);
                await chatEntryPlayer.WhenDonePlaying().WaitAsync(playToken).ConfigureAwait(false);
            }
            catch (Exception e) {
                if (e is not OperationCanceledException)
                    Log.LogError(e, "Playback (reader part) failed in chat #{ChatId}", ChatId);
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

    protected async ValueTask<bool> CanContinuePlayback(CancellationToken cancellationToken)
    {
        if (this is HistoricalChatPlayer)
            await Playback.IsPaused.When(x => !x, cancellationToken).ConfigureAwait(false);

        if (InteractiveUI.IsInteractive.Value)
            return true;

        return await InteractiveUI.Demand(Operation, cancellationToken).ConfigureAwait(false);
    }

    // Protected methods

    protected abstract Task Play(
        ChatEntryPlayer entryPlayer, Moment minPlayAt, CancellationToken cancellationToken);
}
