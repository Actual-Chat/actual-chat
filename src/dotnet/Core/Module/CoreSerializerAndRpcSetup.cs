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
            RpcSerializationFormat.MemoryPackV3,
            RpcSerializationFormat.MemoryPackV3C,
            RpcSerializationFormat.MemoryPackV4,
            RpcSerializationFormat.MemoryPackV4C);

        RpcSerializationFormatResolver.Default = RpcSerializationFormatResolver.Default with {
            DefaultClientFormatKey =
#if DEBUG
                "mempack4",
#else
                isServer ? "mempack4" : "mempack4c",
#endif
        };
    }
}
