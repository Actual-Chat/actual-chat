using System.Diagnostics.CodeAnalysis;
using System.Net;
using ActualChat.Hosting;
using Stl.Fusion.Client;

namespace ActualChat.Notification.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class NotificationClientModule : HostModule
{
    public NotificationClientModule(IServiceProvider services) : base(services) { }

    protected override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.AppKind.IsClient())
            return; // Client-side only module

        var fusion = services.AddFusion();
        fusion.AddClient<INotifications>();
    }
}
