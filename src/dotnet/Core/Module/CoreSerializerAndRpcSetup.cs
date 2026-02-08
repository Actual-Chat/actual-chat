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
            = new(GetFullRpcSerializationFormat().Key);
#else
            = new((isServer ? GetFullRpcSerializationFormat() : GetCompactRpcSerializationFormat()).Key);
#endif
    }

    // Private methods

    private static RpcSerializationFormat GetFullRpcSerializationFormat()
#if USE_MESSAGEPACK
        => RpcSerializationFormat.MessagePackV6;
#else
        => RpcSerializationFormat.MemoryPackV6;
#endif

    private static RpcSerializationFormat GetCompactRpcSerializationFormat()
#if USE_MESSAGEPACK
        => RpcSerializationFormat.MessagePackV6C;
#else
        => RpcSerializationFormat.MemoryPackV6C;
#endif
}
