using ActualChat.Aot;
using ActualChat.UI.Blazor.Internal;

namespace ActualChat.UI.Blazor.Module;

#pragma warning disable CA2255 // Module initializer is intended to be used in...

internal static class BlazorUIModuleInitializer
{
    [ModuleInitializer]
    internal static void ModuleInitializer()
    {
        AotTypes.AddSource(new BlazorUIAotSource());
    }
}
