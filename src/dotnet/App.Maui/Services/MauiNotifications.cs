using ActualChat.Notifications;
using ActualChat.Security;
using ActualLab.Rpc;
using DeviceType = ActualChat.Notifications.DeviceType;

namespace ActualChat.App.Maui.Services;

/// <summary>
/// Handles push notification token registration with the server for mobile devices.
/// </summary>
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
        var command = new Notifications_RegisterDevice { Session = session, DeviceId = token, DeviceType = deviceType };
        await Commander.Call(command, cancellationToken).ConfigureAwait(false);
        Log.LogInformation("<- RefreshNotificationToken");
    }
}
