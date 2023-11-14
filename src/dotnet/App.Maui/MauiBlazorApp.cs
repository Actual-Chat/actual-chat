using ActualChat.App.Maui.Services;
using ActualChat.Security;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.Services;
using Microsoft.AspNetCore.Components;

namespace ActualChat.App.Maui;

public sealed class MauiBlazorApp : AppBase, IAsyncDisposable
{
    [Inject] private Mutable<MauiWebView?> MauiWebViewRef { get; init; } = null!;

    public async ValueTask DisposeAsync()
    {
        try {
            // That's the best we can do here to make sure JS counterpart is dead.
            // We can't use Blazor JS interop here, coz it's already too late.
            var mauiWebView = MauiWebViewRef.Value;
            if (mauiWebView != null)
                await mauiWebView.EvaluateJavaScript("window.ui.BrowserInit.terminate()").SilentAwait();
        }
        catch {
            // Intended
        }
    }

    protected override async Task OnInitializedAsync()
    {
        TrueSessionResolver = Services.GetRequiredService<TrueSessionResolver>();
        var session = await TrueSessionResolver.SessionTask.ConfigureAwait(true);
        MauiWebView.Current!.OnAttach(Services, session);
        try {
            var livenessProbe = MauiLivenessProbe.Current;
            await base.OnInitializedAsync().ConfigureAwait(false);
            livenessProbe ??= MauiLivenessProbe.Current;
            if (livenessProbe != null)
                MauiLivenessProbe.CancelCheck(livenessProbe); // We're ok for sure
        }
        catch (Exception e) {
            Log.LogError(e, "OnInitializedAsync failed, will reload...");
            AppServices.GetRequiredService<ReloadUI>().Reload(); // ReloadUI is a singleton on MAUI
        }
    }
}
