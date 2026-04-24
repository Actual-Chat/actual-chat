using ActualChat.Module;

namespace ActualChat.Testing;

#pragma warning disable CA2255

internal static class TestingModuleInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        RuntimeInfo.IsServer = true;
        ApiContractsModuleInitializer.Load();
        CoreModuleInitializer.Initialize();

#if !USE_MEMORYPACK
        SerializationTestExt.UseMemoryPackSerializer = false;
#endif
    }
}
