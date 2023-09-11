using ActualChat.Security;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui;

public class MauiBlazorApp : AppBase
{
    protected override async Task OnInitializedAsync()
    {
        try {
            TrueSessionResolver = Services.GetRequiredService<TrueSessionResolver>();
            var session = await TrueSessionResolver.SessionTask.ConfigureAwait(true);
            MainPage.Current!.SetupSessionCookie(session);

            try {
                ScopedServices = Services;
            }
            catch (Exception e) {
                Log.LogWarning(e, "OnInitializedAsync: can't set ScopedServices - will restart");
                Services.GetRequiredService<ReloadUI>().Reload();
                return;
            }
            await base.OnInitializedAsync().ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "OnInitializedAsync failed, restarting...");
            Services.GetRequiredService<ReloadUI>().Reload();
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        // Blazor disposes service container scope on page reload.
        // So we must discard ScopedServices (unless they're already discarded)
        // to make sure reload doesn't fail.
        DiscardScopedServices(Services);
    }
}
