using System.Text.Json.Serialization.Metadata;
using ActualChat.Aot;
using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor;

public static class JSRuntimeExt
{
    public static ValueTask OpenNewWindow(this IJSRuntime js, string url)
        => js.InvokeVoidAsync("window.open", url, "_blank", "noopener");

    public static void InjectJsonTypeInfoResolvers(this IJSRuntime jsRuntime)
    {
        CodeKeeper.Keep<JSRuntime>();

        while (jsRuntime is IJSRuntimeWrapper wrapper)
            jsRuntime = wrapper.WrappedJSRuntime;

        // NOTE(AOT): Injection of source-gen JSON contexts into JSRuntime is disabled for now.
        // We need to figure out how to make it work with Blazor, the code below breaks serialization there.
#if false
        var jsRuntimeType = jsRuntime.GetType();
        var optionsProperty = jsRuntimeType.GetProperty(
            "JsonSerializerOptions",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (optionsProperty?.GetValue(jsRuntime) is not JsonSerializerOptions options)
            return;

        // JsonSerializerContext instances cause issues with the TypeInfoResolverChain —
        // even when wrapped to return null for unknown types, their presence changes
        // how the chain resolves types like object[] used internally by JSRuntime.
        // The CodeKeeper string-based keeps handle AOT retention for STJ converter types instead.
        var contexts = AotJsonContexts.All;
        if (contexts.Length > 0)
            options.TypeInfoResolverChain.Insert(0, new SafeCompositeResolver(contexts));
#endif
    }
}
