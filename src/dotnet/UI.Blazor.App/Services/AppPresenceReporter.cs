using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.App.Services;

public class AppPresenceReporter : WorkerBase, IComputeService
{
    public record Options
    {
        public TimeSpan StartDelay { get; init; } = TimeSpan.FromSeconds(1);
    }

    private Session? _session;
    private UserActivityUI? _userActivityUI;
    private ChatAudioUI? _chatAudioUI;
    private ICommander? _commander;
    private MomentClockSet? _clocks;
    private ILogger? _log;
    private IMutableState<Moment> _lastCheckInAt;

    private Options Settings { get; }
    private IServiceProvider Services { get; }
    private Session Session => _session ??= Services.GetRequiredService<Session>();
    private UserActivityUI UserActivityUI => _userActivityUI ??= Services.GetRequiredService<UserActivityUI>();
    private ChatAudioUI ChatAudioUI => _chatAudioUI ??= Services.GetRequiredService<ChatAudioUI>();
    private ICommander Commander => _commander ??= Services.Commander();
    private MomentClockSet Clocks => _clocks ??= Services.Clocks();
    private Moment Now => Clocks.SystemClock.Now;
    private ILogger Log => _log ??= Services.LogFor(GetType());

    public AppPresenceReporter(Options settings, IServiceProvider services)
    {
        Settings = settings;
        Services = services;
        _lastCheckInAt = services.StateFactory().NewMutable(
            Now - Constants.Presence.OfflineTimeout,
            StateCategories.Get(GetType(), nameof(_lastCheckInAt)));
    }

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        await Task.Delay(Settings.StartDelay, cancellationToken).ConfigureAwait(false);

        var cNextCheckInAt = await Computed.Capture(() => GetNextCheckInAt(cancellationToken)).ConfigureAwait(false);
        await foreach (var change in cNextCheckInAt.Changes(cancellationToken).ConfigureAwait(false)) {
            if (change.Value > _lastCheckInAt.Value)
                await CheckIn(Session, cancellationToken).ConfigureAwait(false);
        }
    }

    [ComputeMethod]
    protected virtual async Task<Moment> GetNextCheckInAt(CancellationToken cancellationToken)
    {
        var now = Now;
        var lastCheckInAt = await _lastCheckInAt.Use(cancellationToken).ConfigureAwait(false);
        var nextCheckInAt = lastCheckInAt + Constants.Presence.CheckInPeriod;
        if (nextCheckInAt > now)
            return WithAutoInvalidation(lastCheckInAt);

        // nextCheckInAt <= now
        var activeUntil = await GetActiveUntil(cancellationToken).ConfigureAwait(false);
        return activeUntil > lastCheckInAt ? nextCheckInAt : lastCheckInAt;

        Moment WithAutoInvalidation(Moment result) {
            Computed.GetCurrent()!.Invalidate(nextCheckInAt - now);
            return result;
        }
    }

    [ComputeMethod]
    protected virtual async Task<Moment> GetActiveUntil(CancellationToken cancellationToken)
    {
        var now = Now;
        var activeUntil = await UserActivityUI.ActiveUntil.Use(cancellationToken).ConfigureAwait(false);
        if (activeUntil > now)
            return WithAutoInvalidation(activeUntil);

        var listeningChatIds = await ChatAudioUI.GetListeningChatIds().ConfigureAwait(false);
        if (listeningChatIds.Any())
            return WithAutoInvalidation(now + Constants.Presence.ActivityPeriod);

        var recordingChatId = await ChatAudioUI.GetRecordingChatId().ConfigureAwait(false);
        if (!recordingChatId.IsNone)
            return WithAutoInvalidation(now + Constants.Presence.ActivityPeriod);

        return activeUntil;

        Moment WithAutoInvalidation(Moment result) {
            Computed.GetCurrent()!.Invalidate(result - now);
            return result;
        }
    }

    // Private methods

    private async Task CheckIn(Session session, CancellationToken cancellationToken)
    {
        try {
            await Commander.Call(new IUserPresences.CheckInCommand(session), cancellationToken).ConfigureAwait(false);
            _lastCheckInAt.Value = Now;
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e, "CheckIn failed");
            _lastCheckInAt.Value += Constants.Presence.CheckInRetryDelay;
        }
    }
}
