using ActualChat.Internal;
using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Serialization;

#pragma warning disable CA2255

public static class CoreSerializerAndRpcSetup
{
    [ModuleInitializer]
    internal static void ModuleInitializer()
    {
        // This is super important: TypeRef and some other types which were formerly using Symbol
        // are stored in our DB, and this option enables their legacy serialization mode.
        StringAsSymbolMemoryPackFormatterAttribute.IsEnabled = true;
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<Language>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<StreamId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<MediaId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<Phone>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<UserLinkId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<UserDeviceId>());
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
