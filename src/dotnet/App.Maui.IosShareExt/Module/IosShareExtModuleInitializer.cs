using ActualChat.Aot;

namespace ActualChat.App.Maui.IosShareExt.Module;

#pragma warning disable CA2255

internal static class IosShareExtModuleInitializer
{
    [ModuleInitializer]
    internal static void ModuleInitializer()
        => AotTypes.AddSource(new IosShareExtAotSource());
}
