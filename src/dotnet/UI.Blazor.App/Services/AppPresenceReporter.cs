using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.App.Services;

public class AppPresenceReporter : WorkerBase, IComputeService
{
    public record Options
    {
        public TimeSpan StartDelay { get; init; } = TimeSpan.FromSeconds(1);
        public TimeSpan AwayTimeout { get; init; } = TimeSpan.FromMinutes(3.5);
    }

    private Options Settings { get; }
    private ILogger Log { get; }

    private Session Session { get; }
    private UserActivityUI UserActivityUI { get; }
    private ChatAudioUI ChatAudioUI { get; }
    private ICommander Commander { get; }
    private MomentClockSet Clocks { get; }
    private Moment Now => Clocks.SystemClock.Now;

    public AppPresenceReporter(Options settings, IServiceProvider services)
    {
        Settings = settings;
        Log = services.LogFor(GetType());

        Session = services.GetRequiredService<Session>();
        UserActivityUI = services.GetRequiredService<UserActivityUI>();
        ChatAudioUI = services.GetRequiredService<ChatAudioUI>();
        Commander = services.Commander();
        Clocks = services.Clocks();
    }

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        await Task.Delay(Settings.StartDelay, cancellationToken).ConfigureAwait(false);

        var cState = await Computed.Capture(() => GetAppPresenceState(cancellationToken)).ConfigureAwait(false);
        await foreach (var change in cState.Changes(cancellationToken).ConfigureAwait(false)) {
            var (lastActiveAt, isRecordingOrListening) = change.Value;
            var checkInRecency = Now - lastActiveAt;
            if (checkInRecency < Settings.AwayTimeout || isRecordingOrListening)
                await CheckIn(Session, cancellationToken).ConfigureAwait(false);
            else
                await UserActivityUI.SubscribeForNext(cancellationToken).ConfigureAwait(false);
        }
    }

    [ComputeMethod]
    protected virtual async Task<(Moment LastActiveAt, bool IsRecordingOrListening)> GetAppPresenceState(
        CancellationToken cancellationToken)
    {
        var lastUserActionAt = await UserActivityUI.LastActiveAt.Use(cancellationToken).ConfigureAwait(false);
        var audioStoppedAt = await ChatAudioUI.AudioStoppedAt.Use(cancellationToken).ConfigureAwait(false);
        var recordingChatId = await ChatAudioUI.GetRecordingChatId().ConfigureAwait(false);
        var listeningChatIds = await ChatAudioUI.GetListeningChatIds().ConfigureAwait(false);

        var lastActiveAt = Moment.Max(lastUserActionAt, audioStoppedAt ?? Moment.MinValue);
        var isRecordingOrListening = !recordingChatId.IsNone || listeningChatIds.Any();
        return (lastActiveAt, isRecordingOrListening);
    }

    // Private methods

    private async Task CheckIn(Session session, CancellationToken cancellationToken)
    {
        try {
            await Commander.Call(new IUserPresences.CheckInCommand(session), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e, "CheckIn failed");
        }
    }
}
