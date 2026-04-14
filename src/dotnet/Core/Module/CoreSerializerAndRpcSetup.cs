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
        var useMessagePack = false;
#if USE_MESSAGEPACK
        useMessagePack = true;
#endif
        if (!isServer)
            useMessagePack = false; // Disable MessagePack on the client for now

        if (isServer)
            RpcSerializationFormat.All = ImmutableList.Create(
                RpcSerializationFormat.SystemJsonV5,
                RpcSerializationFormat.SystemJsonV5NP, // Used by the TS RPC client (f=json5np)
                RpcSerializationFormat.MemoryPackV5,
                RpcSerializationFormat.MemoryPackV5C,
                RpcSerializationFormat.MemoryPackV6,
                RpcSerializationFormat.MemoryPackV6C,
                RpcSerializationFormat.MessagePackV6,
                RpcSerializationFormat.MessagePackV6C); // We use MessagePack for efficient binary serialization of media streams
        else
            RpcSerializationFormat.All = ImmutableList.Create(
                RpcSerializationFormat.SystemJsonV5,
                RpcSerializationFormat.SystemJsonV5NP, // Used by the TS RPC client (f=json5np)
                RpcSerializationFormat.MemoryPackV5,
                RpcSerializationFormat.MemoryPackV5C,
                RpcSerializationFormat.MemoryPackV6,
                RpcSerializationFormat.MemoryPackV6C); // TODO(AK): MessagePack serialization does not work for all types yet

        RpcSerializationFormatResolver.Default
#if DEBUG
            = new(GetFullRpcSerializationFormat(useMessagePack).Key);
#else
            = new((isServer ? GetFullRpcSerializationFormat(useMessagePack) : GetCompactRpcSerializationFormat(useMessagePack)).Key);
#endif
    }

    // Private methods

    private static RpcSerializationFormat GetFullRpcSerializationFormat(bool useMessagePack = false)
        => useMessagePack
            ? RpcSerializationFormat.MessagePackV6
            : RpcSerializationFormat.MemoryPackV6;

    private static RpcSerializationFormat GetCompactRpcSerializationFormat(bool useMessagePack = false)
        => useMessagePack
            ? RpcSerializationFormat.MessagePackV6C
            : RpcSerializationFormat.MemoryPackV6C;
}
