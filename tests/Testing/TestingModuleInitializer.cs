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

        // MemoryPack survives only on two legacy read paths (flow state, server KVAS);
        // those are covered by FlowSerializationTestBase and StoredSettingsSerializationTest.
        ActualLab.Testing.SerializationTestExt.UseMemoryPackSerializer = false;
    }
}
