using ActualChat.Hosting;
using ActualChat.UI.Blazor.Services;
using Blazored.Modal;
using Blazored.SessionStorage;
using Microsoft.Extensions.Hosting;
using Stl.OS;
using Stl.Plugins;

namespace ActualChat.UI.Blazor.Module;

public class BlazorUICoreModule : HostModule, IBlazorUIModule
{
    public static string ImportName => "core";

    public BlazorUICoreModule(IPluginInfoProvider.Query _) : base(_) { }
    [ServiceConstructor]
    public BlazorUICoreModule(IPluginHost plugins) : base(plugins) { }

    public override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.BlazorUI))
            return; // Blazor UI only module

        var fusion = services.AddFusion();
        var fusionAuth = fusion.AddAuthentication().AddBlazor();
        // Replace BlazorCircuitContext w/ AppBlazorCircuitContext
        services.AddScoped<BlazorCircuitContext, AppBlazorCircuitContext>();
        services.AddTransient(c => (AppBlazorCircuitContext)c.GetRequiredService<BlazorCircuitContext>());

        // Other UI-related services
        services.AddScoped<DisposeMonitor>();
        services.AddScoped<StateRestore>();
        // Default update delay is 0.2s
        services.AddTransient<IUpdateDelayer>(c =>
            new UpdateDelayer(c.UICommandTracker(), 0.2));

        services.AddBlazoredSessionStorage();
        services.AddBlazoredModal();
        services.AddBlazorContextMenu();
        services.AddScoped<RenderVars>();
        services.AddScoped<Clipboard>();
        services.AddScoped<UserInteractionUI>();
        services.AddScoped<FeedbackUI>();
        services.AddScoped<NavbarUI>();
        services.AddScoped<ImagePreviewUI>();

        if (OSInfo.IsWebAssembly)
            services.AddSingleton<IHostApplicationLifetime, BlazorHostApplicationLifetime>();
    }
}
