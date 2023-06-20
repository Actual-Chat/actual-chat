using System.Diagnostics.CodeAnalysis;
using System.Net;
using ActualChat.Hosting;
using Stl.Fusion.Client;

namespace ActualChat.Chat.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class ChatClientModule : HostModule
{
    public ChatClientModule(IServiceProvider services) : base(services) { }

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
