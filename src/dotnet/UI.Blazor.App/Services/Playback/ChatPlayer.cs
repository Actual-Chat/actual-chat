using ActualChat.MediaPlayback;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Distinguishes between real-time and historical audio playback modes.
/// </summary>
public enum ChatPlayerKind { Listening, Replaying }

/// <summary>
/// Base class for playing audio entries from a chat with pause/resume support.
/// </summary>
public abstract class ChatPlayer : ProcessorBase
{
    // Once enqueued, the playback loop continues, so the larger is this duration,
    // the higher is the chance to enqueue the next entry on time.
    protected static readonly TimeSpan EnqueueAheadDuration = TimeSpan.FromSeconds(2);

    private volatile CancellationTokenSource? _playTokenSource;
    private volatile Task? _whenPlaying;

    protected ILogger Log { get; }
    protected ILogger? DebugLog => DebugMode ? Log : null;
    protected static bool DebugMode => Constants.DebugMode.AudioTrackPlayer;

    protected Session Session => Hub.Session;
    protected IChats Chats => Hub.Chats;
    protected IAuthors Authors => Hub.Authors;
    protected ChatAudioUI ChatAudioUI => Hub.ChatAudioUI;
    protected UserSettingsUI UserSettingsUI => field ??= Hub.Services.GetRequiredService<UserSettingsUI>();
    protected InteractiveUI InteractiveUI => Hub.InteractiveUI;
    protected MomentClockSet Clocks => Hub.Clocks;

    protected IState<TimeSpan> SleepDuration { get; }
    protected IState<TimeSpan> PauseDuration { get; }
    protected TimeSpan SleepAndPauseDuration => SleepDuration.Value + Playback.TotalPauseDuration.Value;

    public AppUIHub Hub { get; }
    public ChatId ChatId { get; }
    public ChatPlayerKind PlayerKind { get; protected init; }
    public Playback Playback { get; }
    public string Operation { get; protected set; } = "";
    public Task? WhenPlaying => _whenPlaying;

    protected ChatPlayer(AppUIHub hub, ChatId chatId)
    {
        Hub = hub;
        ChatId = chatId;
        Log = Hub.LogFor(GetType());

        Playback = Hub.PlaybackFactory.Create();
        Playback.OnTrackStarted += OnPlaybackTrackStarted;
        SleepDuration = Hub.DeviceAwakeUI.TotalSleepDuration;
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

    public async Task<Chat.Chat?> GetChat(CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(Session, ChatId, cancellationToken).ConfigureAwait(false);
        return chat?.Rules.CanRead() == true
            ? chat
            : null;
    }

    public async Task<Task> Start(
        Moment startAt, // Server time
        CancellationToken cancellationToken)
    {
        DebugLog?.LogInformation(
            "ChatPlayer.Start: {PlayerKind} for {ChatId} at {StartAt}",
            PlayerKind, ChatId, startAt);
        this.ThrowIfDisposedOrDisposing();

        CancellationTokenSource playTokenSource;
        CancellationToken playToken;

        var whenPlayingSource = TaskCompletionSourceExt.New<Unit>();
        Task stopTask = Stop();
        var loopCount = 0;
        while (true) {
            loopCount++;
            if (loopCount > 1)
                DebugLog?.LogWarning("ChatPlayer.Start: waiting for previous playback to stop, loop #{LoopCount}", loopCount);
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
        DebugLog?.LogInformation("ChatPlayer.Start: starting background task for {PlayerKind} {ChatId}", PlayerKind, ChatId);

        _ = BackgroundTask.Run(async () => {
            try {
                await Play(Playback, startAt, playToken).ConfigureAwait(false);
                if (PlayerKind == ChatPlayerKind.Listening && !playToken.IsCancellationRequested)
                    Log.LogError(
                        "Play exited normally for a listening player in chat #{ChatId} - this should never happen",
                        ChatId);
            }
            catch (Exception e) {
                if (e is not OperationCanceledException)
                    Log.LogError(e, "Playback (reader part) failed in chat #{ChatId}", ChatId);
            }
            finally {
                // We should wait for playback completion first
                try {
                    await Playback.Abort().WhenCompleted.ConfigureAwait(false);
                }
                catch (Exception e) {
                    if (e is not (OperationCanceledException or ObjectDisposedException))
                        Log.LogError(e, "Failed to abort playback in chat #{ChatId}", ChatId);
                }
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
        if (this is ChatReplayPlayer)
            await Playback.IsPaused.Computed
                .When(x => !x, cancellationToken)
                .ConfigureAwait(false);

        if (InteractiveUI.IsInteractive.Value)
            return true;

        return await InteractiveUI.Demand(Operation, cancellationToken).ConfigureAwait(false);
    }

    public abstract void Pause();
    public abstract Task Resume();

    // Protected methods

    protected abstract Task Play(
        Playback playback, Moment startAt, CancellationToken cancellationToken);

    // Private methods

    private void OnPlaybackTrackStarted(TrackInfo trackInfo, PlayerState state)
    {
        if (trackInfo is not ChatAudioTrackInfo info)
            return;
        if (info.StreamId.IsNullOrEmpty() && info.EntryId == null)
            return;

        _ = ReportPlayback(info, StopToken);
    }

    private async Task ReportPlayback(ChatAudioTrackInfo info, CancellationToken cancellationToken)
    {
        try {
            var alwaysListenedChatIds = await ChatAudioUI.GetChatsYouNeedToKeepListeningTo(cancellationToken)
                .ConfigureAwait(false);
            if (!alwaysListenedChatIds.Contains(ChatId)) {
                var listeningMode = await UserSettingsUI.ChatUserSettings(ChatId)
                    .Get(x => x.ListeningMode, cancellationToken)
                    .ConfigureAwait(false);
                if (listeningMode != ListeningMode.Forever)
                    return;
            }
            await Hub.LiveAudioStreams
                .ReportPlayback(Session, ChatId, info.StreamId, info.EntryId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "ReportPlayback failed in chat #{ChatId}", ChatId);
        }
    }
}
