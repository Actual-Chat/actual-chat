using ActualChat.App.Maui.Services;
using ActualChat.Security;
using ActualChat.UI.Blazor.App;

namespace ActualChat.App.Maui;

public sealed class MauiBlazorApp : AppBase, IDisposable
{
    public void Dispose()
        => TryDiscardActiveScopedServices(Services);

    protected override async Task OnInitializedAsync()
    {
        TrueSessionResolver = Services.GetRequiredService<TrueSessionResolver>();
        var session = await TrueSessionResolver.SessionTask.ConfigureAwait(true);
        MauiWebView.Current!.OnAppInitializing(Services, session);
        try {
            await base.OnInitializedAsync().ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "OnInitializedAsync failed, will reload...");
            AppServices.GetRequiredService<MauiReloadUI>().Reload();
        }
    }
}
