using ActualChat.Hosting.Internal;
using ActualLab.Rpc;
using ActualLab.Rpc.Serialization;

namespace ActualChat.Module;

#pragma warning disable CA2255
#pragma warning disable RCS1003 // Add braces to if-else

/// <summary>
/// Serializer + RPC setup. Nerdbank.MessagePack owns the wire.
/// Use <see cref="Serializers.RegisterShapeProvider"/>,
/// <see cref="Serializers.RegisterStringLikeTypes"/>, or
/// <see cref="Serializers.RegisterConverters"/> in module initializers to extend the wire.
/// <para>
/// Four wire formats are exposed:
/// <c>msgpack6</c>/<c>msgpack6c</c> honor <c>[Key]</c> (array layout for tagged types);
/// <c>msgpack6k</c>/<c>msgpack6ck</c> ignore <c>[Key]</c> on application types so they
/// serialize as name-keyed maps — what JS objects naturally encode to via @msgpack/msgpack.
/// System RPC types (RpcHandshake, Result, ExceptionInfo, …) keep their array wire in both
/// variants because their explicit converters take precedence over the keyless toggle.
/// </para>
/// </summary>
public static partial class CoreModuleInitializer
{
    private static int _isConfigured;

    public static readonly RpcSerializationFormat MessagePackV6 = new("msgpack6",
        () => new RpcByteArgumentSerializerV4(Serializers.MessagePack),
        peer => new RpcByteMessageSerializerV5(peer));
    public static readonly RpcSerializationFormat MessagePackV6C = new("msgpack6c",
        () => new RpcByteArgumentSerializerV4(Serializers.MessagePack),
        peer => new RpcByteMessageSerializerV5Compact(peer));
    public static readonly RpcSerializationFormat MessagePackV6K = new("msgpack6k",
        () => new RpcByteArgumentSerializerV4(Serializers.KeylessMessagePack),
        peer => new RpcByteMessageSerializerV5(peer));
    public static readonly RpcSerializationFormat MessagePackV6CK = new("msgpack6ck",
        () => new RpcByteArgumentSerializerV4(Serializers.KeylessMessagePack),
        peer => new RpcByteMessageSerializerV5Compact(peer));

    // Root of the Load() chain — Core has no upstream module. The empty method body is
    // enough because the explicit static .ctor (implicitly present via our generated half)
    // + [ModuleInitializer] RegisterGenerated ensure the shape provider is registered
    // before any downstream initializer runs.
    public static void Load() { }

    [ModuleInitializer]
    internal static void ModuleInitializer()
    {
        // CoreAotSource + CoreWitness registration are handled by the generated
        // CoreModule.RegisterGenerated (in CoreModule.g.cs). This initializer only owns
        // the pieces the codegen can't derive: the hand-written FrameworkWitness shapes
        // for Fusion framework types and the hand-written converters.
        Serializers.RegisterShapeProvider(FrameworkWitness.GeneratedTypeShapeProvider);
        Serializers.RegisterConverters([new HostRoleNerdbankConverter()]);
    }

    public static void Configure()
    {
        // Idempotent — repeat calls (e.g., test initializer + app startup) are no-ops.
        if (Interlocked.Exchange(ref _isConfigured, 1) != 0)
            return;

        // Force the lazy serializer build to run NOW so every static (ByteSerializer.Default,
        // NerdbankMessagePackByteSerializer.Default*, etc.) is up-to-date by the time this
        // method returns. Without this, code that captures those statics at module init time
        // (ahead of the first Serializers.MessagePack access) would see stale Fusion defaults.
        _ = Serializers.MessagePack;

        var isServer = RuntimeInfo.IsServer;
        if (isServer)
            RpcSerializationFormat.All = ImmutableList.Create(
                RpcSerializationFormat.SystemJsonV5,
                RpcSerializationFormat.SystemJsonV5NP,
                RpcSerializationFormat.MemoryPackV5,  // Legacy
                RpcSerializationFormat.MemoryPackV5C, // Legacy
                RpcSerializationFormat.MemoryPackV6,  // Legacy
                RpcSerializationFormat.MemoryPackV6C, // Legacy
                MessagePackV6,
                MessagePackV6C,
                MessagePackV6K,
                MessagePackV6CK);
        else
            RpcSerializationFormat.All = ImmutableList.Create(
                MessagePackV6,
                MessagePackV6C);
        // Force the resolver to recompute its format set against the new RpcSerializationFormat.All.
        RpcSerializationFormatResolver.DefaultFormats = null!;

        RpcSerializationFormatResolver.Default
#if DEBUG
            = new(MessagePackV6.Key);
#else
            = new((isServer ? MessagePackV6 : MessagePackV6C).Key);
#endif
    }
}
