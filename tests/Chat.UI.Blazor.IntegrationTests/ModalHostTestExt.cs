using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Components;
using Bunit;
using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

public static class ModalHostTestExt
{
    public static IRenderedComponent<ModalHost> RenderModalHost(this BlazorTester tester, AppUIHub hub)
    {
        // What AppBase does for the real app: the hub needs a root component's dispatcher before
        // ModalUI.Show can schedule anything, ModalHost talks to JS on every render,
        // and AttentionUI lets nothing unsolicited in before the first render
        tester.JSInterop.Mode = JSRuntimeMode.Loose;
        tester.Renderer.SetRendererInfo(new RendererInfo("Server", true));
        var host = tester.Render<ModalHost>();
        hub.Initialize(host.Instance, RenderModeDef.GetOrDefault(""));
        hub.LoadingUI.MarkLoaded();
        hub.LoadingUI.MarkRendered();
        return host;
    }
}
