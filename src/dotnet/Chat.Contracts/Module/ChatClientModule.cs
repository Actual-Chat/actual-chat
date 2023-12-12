using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;

namespace ActualChat.Chat.Module;

#pragma warning disable IL2026 // Fine for modules

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class ChatClientModule(IServiceProvider moduleServices) : HostModule(moduleServices)
{
    protected override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.AppKind.IsClient())
            return; // Client-side only module

        var fusion = services.AddFusion();
        fusion.AddClient<IChats>();
        fusion.AddClient<IAuthors>();
        fusion.AddClient<IRoles>();
        fusion.AddClient<IMentions>();
        fusion.AddClient<IReactions>();
    }
}
