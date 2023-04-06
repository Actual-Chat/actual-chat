using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.App.Services;

public class AppPresenceReporterWorker : IComputeService
{
    protected AppPresenceReporter.Options Settings { get; }
    protected ILogger Log { get; }

    protected ISessionResolver SessionResolver { get; }
    protected MomentClockSet Clocks { get; }
    protected UserActivityUI UserActivityUI { get; }
    protected ChatAudioUI ChatAudioUI { get; }
    protected UICommander UICommander { get; }

    public AppPresenceReporterWorker(AppPresenceReporter.Options settings, IServiceProvider services)
    {
        Settings = settings;
        Log = services.LogFor(GetType());

        SessionResolver = services.GetRequiredService<ISessionResolver>();
        Clocks = services.Clocks();
        UserActivityUI = services.GetRequiredService<UserActivityUI>();
        ChatAudioUI = services.GetRequiredService<ChatAudioUI>();
        UICommander = services.GetRequiredService<UICommander>();
    }

    public async Task Run(CancellationToken cancellationToken)
    {
        var session = await SessionResolver.GetSession(cancellationToken).ConfigureAwait(false);
        var cState = await Computed.Capture(() => GetAppPresenceState(cancellationToken)).ConfigureAwait(false);
        await foreach (var change in cState.Changes(cancellationToken).ConfigureAwait(false)) {
            var (lastActiveAt, isRecordingOrListening) = change.Value;
            if (Clocks.SystemClock.Now - lastActiveAt < Settings.AwayTimeout || isRecordingOrListening)
                await UpdatePresence(session, cancellationToken).ConfigureAwait(false);
            else
                await UserActivityUI.SubscribeForNext(cancellationToken).ConfigureAwait(false);
        }
    }

    [ComputeMethod]
    protected virtual async Task<(Moment LastActiveAt, bool IsRecordingOrListening)> GetAppPresenceState(CancellationToken cancellationToken)
    {
        var lastUserActionAt = await UserActivityUI.LastActiveAt.Use(cancellationToken).ConfigureAwait(false);
        var audioStoppedAt = await ChatAudioUI.AudioStoppedAt.Use(cancellationToken).ConfigureAwait(false);
        var recordingChatId = await ChatAudioUI.GetRecordingChatId().ConfigureAwait(false);
        var listeningChatIds = await ChatAudioUI.GetListeningChatIds().ConfigureAwait(false);
        return (Moment.Max(lastUserActionAt, audioStoppedAt ?? Moment.MinValue), !recordingChatId.IsNone || listeningChatIds.Any());
    }

    // Private methods

    private async Task UpdatePresence(Session session, CancellationToken cancellationToken)
    {
        try {
            await UICommander.Run(new IUserPresences.CheckInCommand(session), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e, "UpdatePresence failed");
        }
    }
}
