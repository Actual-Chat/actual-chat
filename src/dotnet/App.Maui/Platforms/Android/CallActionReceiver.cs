using ActualChat.App.Maui.Services;
using ActualChat.Streaming;
using ActualChat.UI.Blazor.App.Services;
using Android.Content;

namespace ActualChat.App.Maui;

[BroadcastReceiver(Exported = false)]
public class CallActionReceiver : BroadcastReceiver
{
    private ILogger Log => field ??= StaticLog.For<CallActionReceiver>();

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent?.Action != IncomingCallNotifications.DeclineAction)
            return;

        var chatId = ChatId.TryParse(intent.GetStringExtra(IncomingCallNotifications.ChatIdExtraKey), allowNull: true);
        if (chatId is null)
            return;

        IncomingCallNotifications.Dismiss(chatId);

        // App alive: decline through the live Blazor scope — the same RPC client (and connection)
        // the in-app banner uses. It also ends the in-app ring. The root container this receiver
        // resolves from may lack the Fusion client stack, so it can't be relied on while alive.
        if (AppServicesAccessor.TryGetScopedServices(out _)) {
            _ = AppServicesAccessor.DispatchToBlazor(
                c => c.GetRequiredService<IncomingCallUI>().Decline(chatId),
                "IncomingCallUI.Decline");
            return;
        }

        // App killed: no Blazor scope — resolve the RPC client from the root container.
        // The RPC call needs async work the receiver's 10s budget must survive.
        var pendingResult = GoAsync();
        _ = BackgroundTask.Run(async () => {
            try {
                var session = await MauiSession.ReadStored().ConfigureAwait(false);
                var liveSessions = IPlatformApplication.Current?.Services.GetService<ILiveSessions>();
                if (session is null || liveSessions is null) {
                    Log.LogWarning("Decline: no session or ILiveSessions client; chat #{ChatId}", chatId);
                    return;
                }
                await liveSessions.DeclineCall(session, chatId, CancellationToken.None).ConfigureAwait(false);
            }
            finally {
                pendingResult?.Finish();
            }
        }, Log, "Decline call failed");
    }
}
