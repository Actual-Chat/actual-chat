using ActualChat.Notification;
using ActualChat.Security;
using ActualLab.Rpc;
using DeviceType = ActualChat.Notification.DeviceType;

namespace ActualChat.App.Maui.Services;

// Called from the root context (not scoped one!)
public class MauiNotifications(IServiceProvider services)
{
    private RpcHub RpcHub { get; } = services.GetRequiredService<RpcHub>();
    private ICommander Commander { get; } = services.GetRequiredService<ICommander>();
    private TrueSessionResolver SessionResolver { get; } = services.GetRequiredService<TrueSessionResolver>();
    private ILogger Log { get; } = services.LogFor<MauiNotifications>();

    public async Task RefreshNotificationToken(string token, DeviceType deviceType, CancellationToken cancellationToken = default)
    {
        Log.LogInformation("-> RefreshNotificationToken");
        await RpcHub.WhenClientPeerConnected(cancellationToken).ConfigureAwait(false);
        Log.LogInformation("RefreshNotificationToken. Peer got connected");
        var session = await SessionResolver.GetSession(cancellationToken).ConfigureAwait(false);
        Log.LogInformation("RefreshNotificationToken. Got session");
        var command = new Notifications_RegisterDevice(session, token, deviceType);
        await Commander.Call(command, cancellationToken).ConfigureAwait(false);
        Log.LogInformation("<- RefreshNotificationToken");
    }
}
