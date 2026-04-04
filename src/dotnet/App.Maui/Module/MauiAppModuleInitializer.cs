using ActualChat.Aot;

namespace ActualChat.App.Maui.Module;

#pragma warning disable CA2255

internal static class MauiAppModuleInitializer
{
    [ModuleInitializer]
    internal static void ModuleInitializer()
        => AotTypes.AddSource(new MauiAppAotSource());
}
