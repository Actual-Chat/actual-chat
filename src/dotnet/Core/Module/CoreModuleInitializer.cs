using ActualChat.Aot;
using ActualChat.Serialization.Internal;
using ActualLab.Rpc;
using ActualLab.Rpc.Serialization;

namespace ActualChat.Module;

#pragma warning disable CA2255

/// <summary>
/// Serializer + RPC setup for Core. Owns the Serializers setup the <see cref="RpcSerializationFormat.All"/> table.
/// Every downstream module initializer calls <see cref="Load"/> to pin its load order ahead of this one.
/// </summary>
public static partial class CoreModuleInitializer
{
    private static volatile int _isConfigured;
    private static readonly Lock Lock = new();

    // Root of the Load() chain — Core has no upstream module.
    public static void Load() { }

    [ModuleInitializer]
    internal static void ModuleInitializer()
        => AotTypes.AddSource(new CoreAotSource());

    public static void Initialize()
    {
        if (Interlocked.Exchange(ref _isConfigured, 1) != 0)
            return;

        lock (Lock) {
            // Default MessagePack serializer
            var defaultOptions = new MessagePackSerializerOptions(AppMessagePackResolver.Instance);
            var defaultMessagePack = new MessagePackByteSerializer(defaultOptions);
            MessagePackByteSerializer.DefaultOptions = defaultOptions;
            Serializers.MessagePack = MessagePackByteSerializer.Default = defaultMessagePack;
            Serializers.MessagePackTypeDecorating = MessagePackByteSerializer.DefaultTypeDecorating
                = new TypeDecoratingByteSerializer(defaultMessagePack);
            ByteSerializer.Default = defaultMessagePack;

            // Keyless MessagePack serializer
            var keylessOptions = new MessagePackSerializerOptions(AppMessagePackKeylessResolver.Instance);
            var keylessMessagePack = new MessagePackByteSerializer(keylessOptions);
            Serializers.KeylessMessagePack = keylessMessagePack;
            Serializers.KeylessMessagePackTypeDecorating = new TypeDecoratingByteSerializer(keylessMessagePack);

            // RPC setup
            var isServer = RuntimeInfo.IsServer;
            if (isServer) {
                var messagePackV6K = new RpcSerializationFormat("msgpack6k",
                    () => new RpcByteArgumentSerializerV4(Serializers.KeylessMessagePack),
                    peer => new RpcByteMessageSerializerV5(peer));
                var messagePackV6CK = new RpcSerializationFormat("msgpack6ck",
                    () => new RpcByteArgumentSerializerV4(Serializers.KeylessMessagePack),
                    peer => new RpcByteMessageSerializerV5Compact(peer));
                RpcSerializationFormat.All = ImmutableList.Create(
                    RpcSerializationFormat.SystemJsonV5,
                    RpcSerializationFormat.SystemJsonV5NP,
                    RpcSerializationFormat.MessagePackV6,
                    RpcSerializationFormat.MessagePackV6_LZ4,
                    RpcSerializationFormat.MessagePackV6C,
                    RpcSerializationFormat.MessagePackV6C_LZ4,
                    messagePackV6K,
                    messagePackV6CK);
            }
            else
                RpcSerializationFormat.All = ImmutableList.Create(
                    RpcSerializationFormat.MessagePackV6,
                    RpcSerializationFormat.MessagePackV6_LZ4,
                    RpcSerializationFormat.MessagePackV6C,
                    RpcSerializationFormat.MessagePackV6C_LZ4);

            RpcSerializationFormatResolver.Default
#if DEBUG
                = new(RpcSerializationFormat.MessagePackV6.Key);
#else
                = new((isServer
                    ? RpcSerializationFormat.MessagePackV6
                    : OSInfo.IsWebAssembly
                        ? RpcSerializationFormat.MessagePackV6C_LZ4 // No outbound compression in WASM
                        : RpcSerializationFormat.MessagePackV6C_LZ4F
                    ).Key);
#endif
        }
    }
}
