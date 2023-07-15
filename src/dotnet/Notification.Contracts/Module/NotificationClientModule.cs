using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;

namespace ActualChat.Notification.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class NotificationClientModule : HostModule
{
    public NotificationClientModule(IServiceProvider moduleServices) : base(moduleServices) { }

    protected override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.AppKind.IsClient())
            return; // Client-side only module

        var fusion = services.AddFusion();
        fusion.AddClient<INotifications>();
    }
}
