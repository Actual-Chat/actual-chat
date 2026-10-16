using ActualChat.UI.Blazor.App;

namespace ActualChat.App.Maui.Services;

/// <summary>
/// The leave the server must hear before the process goes: a killed app sends none, and the server
/// keeps the user in their call until <c>ParticipationDisconnectGrace</c> runs out. Every platform's
/// exit hook calls <see cref="LeaveLiveSessions"/>, which runs the leave once and holds the hook for it.
/// </summary>
public static class MauiAppExit
{
    private static readonly TimeSpan LeaveTimeout = TimeSpan.FromSeconds(3);
    private static readonly Lock Lock = new();
    private static IServiceProvider? _leftScope;
    private static Task? _whenLeftTask;

    private static ILogger Log => field ??= StaticLog.For(typeof(MauiAppExit));

    public static void LeaveLiveSessions()
    {
        // Blocks: the hooks run on the main thread with the OS waiting on them. Task.Run keeps the leave
        // off that thread's synchronization context; the RPC needs the thread pool, not the Blazor dispatcher.
        // Once per Blazor scope rather than per process: a quit raises more than one hook, while on Android
        // the process outlives the activity, and a new activity's scope joins calls of its own.
        Task? whenLeft;
        lock (Lock) {
            if (AppServicesAccessor.TryGetScopedServices(out var scope) && !ReferenceEquals(_leftScope, scope)) {
                _leftScope = scope;
                _whenLeftTask = Task.Run(() => LeaveLiveSessionsInternal(scope));
            }
            whenLeft = _whenLeftTask;
        }
        if (whenLeft is not null && !whenLeft.Wait(LeaveTimeout))
            Log.LogWarning("Leaving the live sessions on exit timed out after {Timeout}", LeaveTimeout.ToShortString());
    }

    // Private methods

    private static async Task LeaveLiveSessionsInternal(IServiceProvider scope)
    {
        try {
            using var cts = new CancellationTokenSource(LeaveTimeout);
            await scope.AppUIHub().CallUI.LeaveOnExit(cts.Token).ConfigureAwait(false);
            Log.LogInformation("Left the live sessions on exit");
        }
        catch (Exception e) {
            Log.LogWarning(e, "Leaving the live sessions on exit failed");
        }
    }
}
