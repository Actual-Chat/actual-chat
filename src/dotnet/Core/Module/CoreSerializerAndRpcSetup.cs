using ActualLab.Rpc;

namespace ActualChat.Module;

#pragma warning disable CA2255

public static class CoreSerializerAndRpcSetup
{
    [ModuleInitializer]
    internal static void ModuleInitializer()
    {
        // Empty for now, but likely to be used in the future
    }

    public static void Configure(bool isServer)
    {
        RpcSerializationFormat.All = ImmutableList.Create(
            RpcSerializationFormat.SystemJsonV5,
            RpcSerializationFormat.MemoryPackV5,
            RpcSerializationFormat.MemoryPackV5C,
            RpcSerializationFormat.MemoryPackV6,
            RpcSerializationFormat.MemoryPackV6C);

        RpcSerializationFormatResolver.Default
#if true // DEBUG
            = new("mempack6");
#else
            = isServer ? new("mempack6") : new("mempack6c");
#endif
    }
}
