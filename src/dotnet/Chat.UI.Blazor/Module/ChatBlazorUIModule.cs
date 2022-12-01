using System.Diagnostics.CodeAnalysis;
using ActualChat.Chat.UI.Blazor.Components.Settings;
using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Hosting;
using ActualChat.UI.Blazor.Events;
using ActualChat.UI.Blazor.Services;
using Stl.Plugins;

namespace ActualChat.Chat.UI.Blazor.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
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

        // Transient
        services.AddTransient<MarkupHub>();

        // Scoped / Blazor Circuit services
        fusion.AddComputeService<RightPanelUI>(ServiceLifetime.Scoped);
        services.AddScoped<NavbarUI>();

        // Chat UI
        fusion.AddComputeService<RightPanelUI>(ServiceLifetime.Scoped);
        fusion.AddComputeService<ChatUI>(ServiceLifetime.Scoped);
        fusion.AddComputeService<ChatPlayers>(ServiceLifetime.Scoped);
        fusion.AddComputeService<ChatUIStateSync>(ServiceLifetime.Scoped);
        fusion.AddComputeService<RecentChatsUI>(ServiceLifetime.Scoped);
        services.AddScoped<PlayableTextPaletteProvider>();
        services.AddScoped<FrontendChatMentionResolverFactory>();

        // Chat activity
        services.AddScoped<ChatActivity>();
        services.AddScoped<ChatUnreadMessagesFactory>();
        services.AddScoped<UnreadMessages>();
        fusion.AddComputeService<ChatRecordingActivity>(ServiceLifetime.Transient);

        // Settings
        services.AddScoped<LanguageUI>();

        services.ConfigureUILifetimeEvents(events => events.OnCircuitContextCreated += RegisterShowSettingsHandler);
    }

    private void RegisterShowSettingsHandler(IServiceProvider services)
    {
        var eventHub = services.GetRequiredService<UIEventHub>();
        eventHub.Subscribe<ShowSettingsEvent>((@event, ct) => {
            var modalUI = services.GetRequiredService<ModalUI>();
            modalUI.Show(new SettingsModal.Model(), true);
            return Task.CompletedTask;
        });
    }
}
