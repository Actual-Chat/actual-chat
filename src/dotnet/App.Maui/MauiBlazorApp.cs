using ActualChat.UI.Blazor.App;

namespace ActualChat.App.Maui;

public class MauiBlazorApp : AppBase
{
    protected override Task OnInitializedAsync()
    {
        SessionProvider.Session = AppSettings.Session;
        ScopedServices = Services;
        return base.OnInitializedAsync();
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
