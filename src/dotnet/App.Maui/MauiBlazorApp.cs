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
            catch (Exception) {
                Log.LogWarning("OnInitializedAsync: can't change ScopedServices - reloading");
                DiscardScopedServices();
                Services.GetRequiredService<ReloadUI>().Reload(false);
                return; // No call to base.OnInitializedAsync() is intended here: reload is all we want
            }
            await base.OnInitializedAsync().ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "OnInitializedAsync failed");
            throw;
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        // On refreshing page, MAUI dispose PageContext.
        // Which dispose Renderer with all components.
        // And after that container is disposed.
        // So we forget previous scoped services container in advance.
        DiscardScopedServices();
    }
}
