using ActualChat.Aot;
using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.App.Module;

#pragma warning disable CA2255 // Module initializer is intended to be used in...

public static partial class BlazorUIAppModuleInitializer
{
    public static void Load() { }

    [ModuleInitializer]
    internal static void ModuleInitializer()
    {
        BlazorUIModuleInitializer.Load();
        AotTypes.AddSource(new BlazorUIAppAotSource());
        AotJsonContexts.Add(BlazorUIAppJsonContext.Default);
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
