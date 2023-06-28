using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor.App;

namespace ActualChat.App.Maui;

public class MauiBlazorApp : AppBase
{
    protected override async Task OnInitializedAsync()
    {
        UI.Blazor.Services.LoadingUI.MarkAppCreated();
        var baseUri = AppSettings.BaseUri;
        var session = await SessionResolver.GetSession().ConfigureAwait(true);
        MainPage.Current!.SetupSessionCookie(baseUri, session);

        ScopedServices = Services;
        await base.OnInitializedAsync().ConfigureAwait(false);
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
