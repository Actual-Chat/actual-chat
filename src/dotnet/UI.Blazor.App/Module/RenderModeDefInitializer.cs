namespace ActualChat.UI.Blazor.App.Module;

#pragma warning disable CA2255 // Module initializer is intended to be used in...

internal static class RenderModeDefInitializer
{
    [ModuleInitializer]
    internal static void ModuleInitializer()
    {
        RenderModeDef.All = [
            new("a", "Auto") { Mode = new InteractiveAutoRenderMode(prerender: false) },
            new("w", "WASM") { Mode = new InteractiveWebAssemblyRenderMode(prerender: false) },
            new("s", "Server") { Mode = new InteractiveServerRenderMode(prerender: true) },
            new("sp", "Server Prerendered") { Mode = new InteractiveServerRenderMode(prerender: true) },
            new("ss", "Server Static") { Mode = null! },
            new("m", "MAUI") { Mode = null! },
        ];
        RenderModeDef.Default = RenderModeDef.All[0];
    }
}
