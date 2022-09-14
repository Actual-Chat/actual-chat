using ActualChat.Chat.UI.Blazor.Components.Settings;
using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Chat.UI.Blazor.Testing;
using ActualChat.Hosting;
using ActualChat.Kvas;
using ActualChat.Search;
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

        // Transient
        services.AddTransient<MarkupHub>();
        services.AddTransient<MarkupEditorHtmlConverter>();

        // Navbar widgets
        services.RegisterNavbarWidget<ChatListNavbarWidget>(navbarGroupId: ChatListNavbarWidget.NavbarGroupId);
        services.RegisterNavbarWidget<ContactListNavbarWidget>(navbarGroupId: ContactListNavbarWidget.NavbarGroupId);

        // Scoped / Blazor Circuit services
        fusion.AddComputeService<RightPanelUI>(ServiceLifetime.Scoped);

        // Chat UI
        fusion.AddComputeService<RightPanelUI>(ServiceLifetime.Scoped);
        fusion.AddComputeService<ChatUI>(ServiceLifetime.Scoped);
        fusion.AddComputeService<ChatPlayers>(ServiceLifetime.Scoped);
        fusion.AddComputeService<ChatUIStateSync>(ServiceLifetime.Scoped);
        fusion.AddComputeService<RecentChats>(ServiceLifetime.Scoped);
        services.AddScoped<PlayableTextPaletteProvider>();
        services.AddScoped<FrontendChatMentionResolverFactory>();

        // Chat activity
        services.AddScoped<ChatActivity>();
        services.AddScoped<UnreadMessagesFactory>();
        fusion.AddComputeService<ChatRecordingActivity>(ServiceLifetime.Transient);

        services.ConfigureLifetimeEvents(events =>
            events.OnCircuitContextCreated += svp => RegisterShowSettingsHandler(svp)
        );
    }

    private void RegisterShowSettingsHandler(IServiceProvider services)
    {
        var eventHub = services.GetRequiredService<UIEventHub>();
        var modalUI = services.GetRequiredService<ModalUI>();
        eventHub.Subscribe<ShowSettingsModal>((@event, ct) => {
            modalUI.Show(new SettingsModal.Model(), "modal-full");
            return Task.CompletedTask;
        });
    }
}
