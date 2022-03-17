using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Chat.UI.Blazor.Testing;
using ActualChat.Hosting;
using ActualChat.MediaPlayback;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stl.Plugins;

namespace ActualChat.Chat.UI.Blazor.Module;

public class ChatBlazorUIModule : HostModule, IBlazorUIModule
{
    public static string ImportName => "chat";

    public ChatBlazorUIModule(IPluginInfoProvider.Query _) : base(_) { }

    [ServiceConstructor]
    public ChatBlazorUIModule(IPluginHost plugins) : base(plugins) { }

    public override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.BlazorUI))
            return; // Blazor UI only module

        var fusion = services.AddFusion();

        // Singletons
        services.TryAddSingleton<IChatMediaResolver, BuiltInChatMediaResolver>();
        fusion.AddComputeService<VirtualListTestService>();

        // Scoped / Blazor Circuit services
        services.TryAddScoped<IChatPlayerFactory, ChatPlayerFactory>();
        fusion.AddComputeService<ChatController>(ServiceLifetime.Scoped);
        fusion.AddComputeService<ChatActivities>(ServiceLifetime.Scoped);
        fusion.AddComputeService<ChatPlayers>(ServiceLifetime.Scoped);

        services.RegisterNavItems<ChatLinks>();
        services.TryAddScoped<ListeningChatsList>();
    }
}
