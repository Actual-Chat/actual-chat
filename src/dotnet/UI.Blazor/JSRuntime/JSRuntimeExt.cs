using ActualChat.Aot;
using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor;

/// <summary>
/// Configures the <see cref="IJSRuntime"/> JSON serializer options
/// to include our source-generated <see cref="BlazorUIJsonContext"/>
/// for Native AOT compatibility.
/// </summary>
public static class JSRuntimeExt
{
    public static void InjectJsonTypeInfoResolvers(this IJSRuntime jsRuntime)
    {
        CodeKeeper.Keep<JSRuntime>();

        while (jsRuntime is IJSRuntimeWrapper wrapper)
            jsRuntime = wrapper.WrappedJSRuntime;

        var jsRuntimeType = jsRuntime.GetType();
        var optionsProperty = jsRuntimeType.GetProperty(
            "JsonSerializerOptions",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (optionsProperty?.GetValue(jsRuntime) is not JsonSerializerOptions options)
            return;

        // Append after the default resolver: JsonSerializerContext throws (rather than
        // returning null) for types it doesn't know, so it can't go first in the chain.
        var contexts = AotJsonContexts.All;
        var typeInfoResolverChain = options.TypeInfoResolverChain;
        for (var i = 0; i < contexts.Length; i++)
            typeInfoResolverChain.Insert(i, contexts[i]);
    }
}
