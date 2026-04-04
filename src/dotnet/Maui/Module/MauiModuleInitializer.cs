using ActualChat.Aot;

namespace ActualChat.Maui.Module;

#pragma warning disable CA2255

internal static class MauiModuleInitializer
{
    [ModuleInitializer]
    internal static void ModuleInitializer()
    {
        AotTypes.AddSource(new MauiAotSource());
    }
}
