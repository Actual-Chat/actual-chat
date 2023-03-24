using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.App.Services;

public class AppPresenceReporterWorker : IComputeService
{
    protected AppPresenceReporter.Options Settings { get; }
    protected ILogger Log { get; }

    protected IAuth Auth { get; }
    protected IAccounts Accounts { get; }
    protected ISessionResolver SessionResolver { get; }
    protected MomentClockSet Clocks { get; }
    protected UserActivityUI UserActivityUI { get; }
    protected IUserPresences UserPresences { get; }
    protected ChatAudioUI ChatAudioUI { get; }

    public AppPresenceReporterWorker(AppPresenceReporter.Options settings, IServiceProvider services)
    {
        Settings = settings;
        Log = services.LogFor(GetType());

        Auth = services.GetRequiredService<IAuth>();
        Accounts = services.GetRequiredService<IAccounts>();
        SessionResolver = services.GetRequiredService<ISessionResolver>();
        Clocks = Settings.Clocks ?? services.Clocks();
        UserActivityUI = services.GetRequiredService<UserActivityUI>();
        UserPresences = services.GetRequiredService<IUserPresences>();
        ChatAudioUI = services.GetRequiredService<ChatAudioUI>();
    }

    public async Task Run(CancellationToken cancellationToken)
    {
        var session = await SessionResolver.GetSession(cancellationToken).ConfigureAwait(false);
        var cState = await Computed.Capture(() => GetAppPresenceState(cancellationToken)).ConfigureAwait(false);
        await foreach (var change in cState.Changes(cancellationToken).ConfigureAwait(false)) {
            var (lastActiveAt, isRecording) = change.Value;
            if (Clocks.SystemClock.Now - lastActiveAt < Settings.AwayTimeout || isRecording)
                await UpdatePresence(session, cancellationToken).ConfigureAwait(false);
            else {
                await InvalidatePresence(session, cancellationToken).ConfigureAwait(false);
                await UserActivityUI.SubscribeForNext(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    [ComputeMethod]
    protected virtual async Task<(Moment lastActiveAt, bool IsRecording)> GetAppPresenceState(CancellationToken cancellationToken)
    {
        var lastActiveAt = await UserActivityUI.LastActiveAt.Use(cancellationToken).ConfigureAwait(false);
        var recordingChatId = await ChatAudioUI.GetRecordingChatId().ConfigureAwait(false);
        return (lastActiveAt, !recordingChatId.IsNone);
    }

    // Private methods

    private async Task UpdatePresence(Session session, CancellationToken cancellationToken)
    {
        try {
            await Auth.UpdatePresence(session, cancellationToken).ConfigureAwait(false);
            await InvalidatePresence(session, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e, "UpdatePresence failed");
        }
    }

    private async Task InvalidatePresence(Session session, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        using (Computed.Invalidate())
            _ = UserPresences.Get(account.Id, cancellationToken);
    }
}
