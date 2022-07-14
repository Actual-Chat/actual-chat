using ActualChat.App.Wasm;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using Stl.Fusion.Blazor;
using Stl.Fusion.Server.Authentication;
using Stl.Fusion.Server.Controllers;

namespace ActualChat.Host.Pages;

public static class HtmlHelperExt
{
    public static Task<IHtmlContent> RenderApp(
        this IHtmlHelper html, IServiceProvider services, HttpContext httpContext)
    {
        var serverAuthHelper = services.GetRequiredService<ServerAuthHelper>();
        var isServerSideBlazor = BlazorModeController.IsServerSideBlazor(httpContext);
        var sessionId = serverAuthHelper.Session.Id.Value;

        return html.RenderComponentAsync<Main>(
            isServerSideBlazor ? RenderMode.Server : RenderMode.WebAssembly,
            new { SessionId = sessionId });
    }

    public static async Task<IHtmlContent> PrerenderApp(
        this IHtmlHelper html, IServiceProvider services, HttpContext httpContext)
    {
        var circuitContext = services.GetRequiredService<BlazorCircuitContext>();
        var serverAuthHelper = services.GetRequiredService<ServerAuthHelper>();
        var isServerSideBlazor = BlazorModeController.IsServerSideBlazor(httpContext);
        var sessionId = serverAuthHelper.Session.Id.Value;

        using var prerendering = circuitContext.Prerendering();
        var errorCount = 0;
        while (true) {
            // Workaround for https://github.com/dotnet/aspnetcore/issues/26966
            try {
                return await html.RenderComponentAsync<Main>(
                    isServerSideBlazor ? RenderMode.ServerPrerendered : RenderMode.WebAssemblyPrerendered,
                    new { SessionId = sessionId }
                    ).ConfigureAwait(false);
            }
            catch (InvalidOperationException) {
                if (++errorCount > 3)
                    throw;
            }
        }
    }
}
