using System.Diagnostics.CodeAnalysis;
using ActualChat.Audio;
using ActualChat.Chat.UI.Blazor.Components.Settings;
using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Chat.UI.Blazor.Testing;
using ActualChat.Hosting;
using ActualChat.UI.Blazor.Events;
using ActualChat.UI.Blazor.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        if (!HostInfo.AppKind.HasBlazorUI())
            return; // Blazor UI only module

        var fusion = services.AddFusion();

        // Singletons
        fusion.AddComputeService<VirtualListTestService>();

        // Scoped / Blazor Circuit services
        fusion.AddComputeService<RightPanelUI>(ServiceLifetime.Scoped);
        services.AddScoped<NavbarUI>(c => new NavbarUI(c));
        services.AddScoped<AuthorUI>(c => new AuthorUI(c));
        services.AddScoped<IAudioOutputController>(c => new AudioOutputController(c));
        services.AddScoped(c => new CachingKeyedFactory<IChatMarkupHub, ChatId, ChatMarkupHub>(c, 256).ToGeneric());

        // Chat UI
        services.AddTransient<IdleAudioMonitor>(c => new IdleAudioMonitor(c));
        fusion.AddComputeService<RightPanelUI>(ServiceLifetime.Scoped);
        fusion.AddComputeService<ChatAudioUI>(ServiceLifetime.Scoped);
        fusion.AddComputeService<ActiveChatsUI>(ServiceLifetime.Scoped);
        fusion.AddComputeService<ChatUI>(ServiceLifetime.Scoped);
        fusion.AddComputeService<ChatPlayers>(ServiceLifetime.Scoped);
        services.AddScoped<PlayableTextPaletteProvider>(_ => new PlayableTextPaletteProvider());

        // Chat activity
        services.AddScoped<ChatActivity>(c => new ChatActivity(c));
        fusion.AddComputeService<ChatRecordingActivity>(ServiceLifetime.Transient);

        // Settings
        services.TryAddSingleton<AudioSettings>(c => new AudioSettings());
        services.AddScoped<LanguageUI>(c => new LanguageUI(c));
        services.AddScoped<OnboardingUI>(c => new OnboardingUI(c));

        services.ConfigureUILifetimeEvents(events => events.OnCircuitContextCreated += RegisterShowSettingsHandler);
    }

    private void RegisterShowSettingsHandler(IServiceProvider services)
    {
        var eventHub = services.UIEventHub();
        eventHub.Subscribe<ShowSettingsEvent>((@event, ct) => {
            var modalUI = services.GetRequiredService<ModalUI>();
            modalUI.Show(new SettingsModal.Model(), true);
            return Task.CompletedTask;
        });
    }
}
