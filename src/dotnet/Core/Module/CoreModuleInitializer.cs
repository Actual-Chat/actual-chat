using ActualChat.Aot;
using ActualLab.Rpc;

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
            var isServer = RuntimeInfo.IsServer;
            if (isServer)
                RpcSerializationFormat.All = ImmutableList.Create(
                    RpcSerializationFormat.SystemJsonV5,
                    RpcSerializationFormat.SystemJsonV5NP,
                    RpcSerializationFormat.MemoryPackV5, // Legacy clients
                    RpcSerializationFormat.MemoryPackV5C, // Legacy clients
                    RpcSerializationFormat.MemoryPackV6,
                    RpcSerializationFormat.MemoryPackV6C,
                    RpcSerializationFormat.MessagePackV6,
                    RpcSerializationFormat.MessagePackV6C);
            else
                RpcSerializationFormat.All = ImmutableList.Create(
                    RpcSerializationFormat.MessagePackV6,
                    RpcSerializationFormat.MessagePackV6C);

            RpcSerializationFormatResolver.Default
#if DEBUG
                = new(RpcSerializationFormat.MessagePackV6.Key);
#else
                = new((isServer ? RpcSerializationFormat.MessagePackV6 : RpcSerializationFormat.MessagePackV6C).Key);
#endif
        }
    }
}
