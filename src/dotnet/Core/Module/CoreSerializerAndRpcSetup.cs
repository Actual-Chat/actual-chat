using ActualChat.Aot;
using ActualLab.Rpc;
using ActualLab.Serialization.Internal;
using MessagePack;
using MessagePack.Resolvers;

namespace ActualChat.Module;

#pragma warning disable CA2255
#pragma warning disable CS0465 // 'Finalize' on a static class cannot interfere with destructor invocation

public static class CoreSerializerAndRpcSetup
{
    private static readonly Lock Lock = new();
    private static readonly List<IFormatterResolver> PrependResolvers = new();
    private static readonly List<IFormatterResolver> GeneratedResolvers = new();
    private static bool _isConfigured;

    [ModuleInitializer]
    internal static void ModuleInitializer()
        => AotTypes.AddSource(new CoreAotSource());

    public static void Configure(bool isServer)
    {
        lock (Lock) {
            _isConfigured = true;
            RebuildResolverChain();
        }
#if USE_MESSAGEPACK
        var useMessagePack = true;
#else
        var useMessagePack = false;
#endif
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
#if !USE_MESSAGEPACK
                RpcSerializationFormat.MemoryPackV6,
                RpcSerializationFormat.MemoryPackV6C,
#endif
                RpcSerializationFormat.MessagePackV6,
                RpcSerializationFormat.MessagePackV6C);

        RpcSerializationFormatResolver.Default
#if DEBUG
            = new(GetFullRpcSerializationFormat(useMessagePack).Key);
#else
            = new((isServer ? GetFullRpcSerializationFormat(useMessagePack) : GetCompactRpcSerializationFormat(useMessagePack)).Key);
#endif
    }

    public static void AddPrependResolver(IFormatterResolver resolver)
    {
        lock (Lock) {
            PrependResolvers.Add(resolver);
            if (_isConfigured)
                RebuildResolverChain();
        }
    }

    public static void AddGeneratedResolver(IFormatterResolver resolver)
    {
        lock (Lock) {
            GeneratedResolvers.Add(resolver);
            if (_isConfigured)
                RebuildResolverChain();
        }
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

    private static void RebuildResolverChain()
    {
        var chain = new IFormatterResolver[PrependResolvers.Count + GeneratedResolvers.Count + 1];
        PrependResolvers.CopyTo(chain, 0);
        GeneratedResolvers.CopyTo(chain, PrependResolvers.Count);
        // StandardResolver is the last-resort fallback. On JIT it handles types via
        // dynamic IL emit; on NativeAOT those paths throw, so all user types must be
        // covered by the generated resolvers registered above.
        chain[^1] = StandardResolver.Instance;
        DefaultMessagePackResolver.Resolvers = chain;
    }
}
