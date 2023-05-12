using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.App;
using Microsoft.JSInterop;

namespace ActualChat.App.Maui;

public class MauiBlazorApp : AppBase
{
    protected override async Task OnInitializedAsync()
    {
        var baseUri = AppSettings.BaseUri;
        var session = await SessionProvider.GetSession().ConfigureAwait(true);
        MainPage.Current!.SetupSessionCookie(baseUri, session);
        await InitPage(baseUri, session).ConfigureAwait(true);

        ScopedServices = Services;
        await base.OnInitializedAsync().ConfigureAwait(true);
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

    private async Task InitPage(Uri baseUri, Session session)
    {
        using var _ = Tracer.Region("window.App.initPage JS call");
        var js = Services.GetRequiredService<IJSRuntime>();
        var script = $"window.App.initPage('{baseUri}', '{session.Hash}')";
        await js.EvalVoid(script).ConfigureAwait(false);
    }
}
