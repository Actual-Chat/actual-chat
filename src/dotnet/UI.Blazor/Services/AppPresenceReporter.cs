using ActualChat.Users;

namespace ActualChat.UI.Blazor.Services;

public class AppPresenceReporter : WorkerBase
{
    public record Options
    {
        public TimeSpan AwayTimeout { get; init; } = TimeSpan.FromMinutes(3.5);
        public MomentClockSet? Clocks { get; init; }
    }

    protected Options Settings { get; }
    protected ILogger Log { get; }

    protected IAuth Auth { get; }
    protected ISessionResolver SessionResolver { get; }
    protected MomentClockSet Clocks { get; }
    protected UserActivityUI UserActivityUI { get; }
    protected IUserPresences UserPresences { get; }

    public AppPresenceReporter(Options settings, IServiceProvider services)
    {
        Settings = settings;
        Log = services.LogFor(GetType());

        Auth = services.GetRequiredService<IAuth>();
        SessionResolver = services.GetRequiredService<ISessionResolver>();
        Clocks = Settings.Clocks ?? services.Clocks();
        UserActivityUI = services.GetRequiredService<UserActivityUI>();
        UserPresences = services.GetRequiredService<IUserPresences>();
    }


    protected override async Task RunInternal(CancellationToken cancellationToken)
    {
        var session = await SessionResolver.GetSession(cancellationToken).ConfigureAwait(false);
        await foreach (var change in UserActivityUI.LastActiveAt.Changes(cancellationToken)) {
            var lastActiveAt = change.Value;
            if (Clocks.SystemClock.Now - lastActiveAt < Settings.AwayTimeout)
                await UpdatePresence(session, cancellationToken).ConfigureAwait(false);
            else {
                await InvalidatePresence(session, cancellationToken);
                await UserActivityUI.SubscribeForNext(cancellationToken);
            }
        }
    }

    // Private methods

    private async Task UpdatePresence(Session session, CancellationToken cancellationToken)
    {
        try {
            await Auth.UpdatePresence(session, cancellationToken).ConfigureAwait(false);
            await InvalidatePresence(session, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e, "UpdatePresence failed");
        }
    }

    private async Task InvalidatePresence(Session session, CancellationToken cancellationToken)
    {
        var user = await Auth.GetUser(session, cancellationToken);
        if (user == null)
            return;

        using (Computed.Invalidate())
            _ = UserPresences.Get(user.Id, cancellationToken);
    }
}
