using ActualLab.Testing;

namespace ActualChat.Testing;

#pragma warning disable CA2255

internal static class TestingModuleInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
#if !USE_MESSAGEPACK
        SerializationTestExt.UseMessagePackSerializer = false;
#endif
#if !USE_MEMORYPACK
        SerializationTestExt.UseMemoryPackSerializer = false;
#endif
    }
}
