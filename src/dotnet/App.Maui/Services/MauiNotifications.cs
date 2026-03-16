using ActualChat.Notification;
using ActualChat.Security;
using ActualLab.Rpc;
using DeviceType = ActualChat.Notification.DeviceType;

namespace ActualChat.App.Maui.Services;

/// <summary>
/// Handles push notification token registration with the server for mobile devices.
/// </summary>
// Called from the root context (not scoped one!)
public class MauiNotifications(IServiceProvider services)
{
    private RpcHub RpcHub => field ??= services.RpcHub();
    private ICommander Commander => field ??= services.Commander();
    private TrueSessionResolver SessionResolver => field ??= services.GetRequiredService<TrueSessionResolver>();
    private ILogger Log => field ??= services.LogFor(GetType());

    public async Task RefreshNotificationToken(string token, DeviceType deviceType, NotificationChannel notificationChannel, CancellationToken cancellationToken = default)
    {
        Log.LogInformation("-> RefreshNotificationToken");
        await RpcHub.WhenClientPeerConnected(cancellationToken).ConfigureAwait(false);
        Log.LogInformation("RefreshNotificationToken. Peer got connected");
        var session = await SessionResolver.GetSession(cancellationToken).ConfigureAwait(false);
        Log.LogInformation("RefreshNotificationToken. Got session");
        var command = new Notifications_RegisterDevice(session, token, deviceType) {
            NotificationChannel = notificationChannel,
        };
        await Commander.Call(command, cancellationToken).ConfigureAwait(false);
        Log.LogInformation("<- RefreshNotificationToken");
    }
}
