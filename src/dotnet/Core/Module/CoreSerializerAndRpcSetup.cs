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
            RpcSerializationFormat.SystemJsonV3,
            RpcSerializationFormat.MemoryPackV2,
            RpcSerializationFormat.MemoryPackV2C,
            RpcSerializationFormat.MemoryPackV2NP,
            RpcSerializationFormat.MemoryPackV2CNP,
            RpcSerializationFormat.MemoryPackV3,
            RpcSerializationFormat.MemoryPackV3C);

        RpcSerializationFormatResolver.Default = RpcSerializationFormatResolver.Default with {
            DefaultClientFormatKey =
#if DEBUG
                "mempack3",
#else
                isServer ? "mempack3" : "mempack3c",
#endif
        };
    }
}
