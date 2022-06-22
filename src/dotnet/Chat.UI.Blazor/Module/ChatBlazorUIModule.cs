using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Chat.UI.Blazor.Testing;
using ActualChat.Hosting;
using ActualChat.UI.Blazor.Events;
using ActualChat.UI.Blazor.Services;
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
        services.TryAddSingleton<IChatMediaResolver, ChatMediaResolver>();
        fusion.AddComputeService<VirtualListTestService>();

        // Navbar widgets
        services.RegisterNavbarWidget<ChatListNavbarWidget>();
        services.RegisterNavbarWidget<ContactListNavbarWidget>();

        // Scoped / Blazor Circuit services
        fusion.AddComputeService<RightPanelUI>(ServiceLifetime.Scoped);

        // Chat UI
        fusion.AddComputeService<ChatUI>(ServiceLifetime.Scoped);
        fusion.AddComputeService<ChatPlayers>(ServiceLifetime.Scoped);
        fusion.AddComputeService<ChatUIStateSync>(ServiceLifetime.Scoped);
        services.AddStateRestoreHandler<ChatUIStatePersister>();

        // Chat activity
        services.AddScoped<ChatActivity>();
        services.AddScoped<UnreadMessagesFactory>();
        fusion.AddComputeService<ChatRecordingActivity>(ServiceLifetime.Transient);
    }
}
