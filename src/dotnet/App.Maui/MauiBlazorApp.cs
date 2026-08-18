using ActualChat.App.Maui.Services;
using ActualChat.Security;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui;

public sealed class MauiBlazorApp : AppBase
{
    private MauiWebView? _mauiWebView;
    private MauiWebViewPageContextTracker? _pageContextTracker;

    static MauiBlazorApp()
        => OtherUIAssemblies = [typeof(UIHub).Assembly, typeof(MauiBlazorApp).Assembly];

    public override void Dispose()
    {
        Log.LogInformation("Dispose MauiBlazorApp");
        _pageContextTracker?.OnMauiBlazorAppDisposing();
        _mauiWebView?.ResetScopedServices(Services);
        base.Dispose();
    }

    protected override async Task OnInitializedAsync()
    {
        _pageContextTracker = Services.GetRequiredService<MauiWebViewPageContextTracker>();
        _mauiWebView = MauiWebView.Current;
        _pageContextTracker.AttachTo(_mauiWebView);
        TrueSessionResolver = Services.GetRequiredService<TrueSessionResolver>();
        var session = await TrueSessionResolver.SessionTask.ConfigureAwait(true);
        if (_mauiWebView != null)
            await _mauiWebView.SetScopedServices(Services, session).ConfigureAwait(true);
#if IOS
        _ = Services.GetService<IosPttUI>();
#endif

        FirebaseAnalyticsExt.ActivateOwnAnalyticsCollection(Services);
        _ = Services.GetRequiredService<MauiSentryInitializer>().Start();

        // Uncomment to gather Fusion stats for profiling
        // var debugUI = Services.GetRequiredService<DebugUI>();
        // debugUI.StartFusionMonitor();
        // debugUI.StartTaskMonitor();
        try {
            await base.OnInitializedAsync().ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "OnInitializedAsync failed, will reload...");
            Services.GetRequiredService<ReloadUI>().Reload(); // ReloadUI is a singleton on MAUI
        }
    }
}
