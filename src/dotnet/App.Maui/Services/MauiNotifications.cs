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

    public async Task RefreshNotificationToken(string token, DeviceType deviceType, CancellationToken cancelationToken = default)
    {
        await RpcHub.WhenClientPeerConnected(cancelationToken).ConfigureAwait(false);
        var session = await SessionResolver.GetSession(cancelationToken).ConfigureAwait(false);
        var command = new Notifications_RegisterDevice(session, token, deviceType);
        await Commander.Call(command, cancelationToken).ConfigureAwait(false);
    }
}
