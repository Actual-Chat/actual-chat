using ActualChat.App.Maui.Services;
using ActualChat.Security;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.Services;
using Microsoft.AspNetCore.Components;

namespace ActualChat.App.Maui;

public sealed class MauiBlazorApp : AppBase, IDisposable
{
    private MauiWebView? _mauiWebView;

    [Inject] private Mutable<MauiWebView?> MauiWebViewRef { get; init; } = null!;
    [Inject] private ScopedServicesDisposeTracker ScopedServicesDisposeTracker { get; init; } = null!;

    public void Dispose()
        => _mauiWebView?.ResetScopedServices(Services);

    protected override async Task OnInitializedAsync()
    {
        _mauiWebView = MauiWebView.Current;
        TrueSessionResolver = Services.GetRequiredService<TrueSessionResolver>();
        var session = await TrueSessionResolver.SessionTask.ConfigureAwait(true);
        _mauiWebView?.SetScopedServices(Services, session);

        // Uncomment to gather Fusion stats for profiling
        // var debugUI = Services.GetRequiredService<DebugUI>();
        // debugUI.StartFusionMonitor();
        // debugUI.StartTaskMonitor();
        try {
            await base.OnInitializedAsync().ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "OnInitializedAsync failed, will reload...");
            AppServices.GetRequiredService<ReloadUI>().Reload(); // ReloadUI is a singleton on MAUI
        }
    }
}
