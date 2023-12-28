using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;
using ActualChat.MediaPlayback;
using ActualChat.Permissions;

namespace ActualChat.Video.UI.Blazor.Module;

#pragma warning disable IL2026 // Fine for modules

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class VideoBlazorUIModule(IServiceProvider moduleServices)
    : HostModule(moduleServices), IBlazorUIModule
{
    public static string ImportName => "video";

    protected override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.AppKind.HasBlazorUI())
            return; // Blazor UI only module

        services.AddFusion();
    }
}
