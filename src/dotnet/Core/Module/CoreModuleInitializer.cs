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
    private static int _isConfigured;
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
            var clientFormats = ImmutableList.Create(
                RpcSerializationFormat.MessagePackV6,
                RpcSerializationFormat.MessagePackV6_LZ4,
                RpcSerializationFormat.MessagePackV6_LZ4F,
                RpcSerializationFormat.MessagePackV6C,
                RpcSerializationFormat.MessagePackV6C_LZ4,
                RpcSerializationFormat.MessagePackV6C_LZ4F);

            // RPC setup
            var isServer = RuntimeInfo.IsServer;
            if (isServer) {
                // Every inbound msgpack format is wrapped so pre-Uuid clients' commands still deserialize.
                var messagePackV6K = new RpcSerializationFormat("msgpack6k",
                    () => new ApiCommandRpcArgumentSerializer(Serializers.KeylessMessagePack),
                    peer => new RpcByteMessageSerializerV5(peer));
                var messagePackV6CK = new RpcSerializationFormat("msgpack6ck",
                    () => new ApiCommandRpcArgumentSerializer(Serializers.KeylessMessagePack),
                    peer => new RpcByteMessageSerializerV5Compact(peer));
                RpcSerializationFormat.All = clientFormats
                    .ConvertAll(f => WithApiCommandCompat(f, Serializers.MessagePack))
                    .AddRange([
                        RpcSerializationFormat.SystemJsonV5,
                        RpcSerializationFormat.SystemJsonV5NP,
                        messagePackV6K,
                        messagePackV6CK,
                    ]);
            }
            else
                RpcSerializationFormat.All = clientFormats;

            // The resolver caches DefaultFormats on its first read, which may happen before this runs -
            // so assigning RpcSerializationFormat.All alone doesn't stop clients from pinning ?f=mempack*.
            RpcSerializationFormatResolver.DefaultFormats = RpcSerializationFormat.All;

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

    // Private methods

    private static RpcSerializationFormat WithApiCommandCompat(
        RpcSerializationFormat format, IByteSerializer baseSerializer)
        => new(format.Key,
            () => new ApiCommandRpcArgumentSerializer(baseSerializer),
            format.MessageSerializerFactory,
            format.CompressionFormat,
            format.CompressionMode);
}
