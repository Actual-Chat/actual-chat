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
    private static bool _isServer;

    [ModuleInitializer]
    internal static void ModuleInitializer()
        => AotTypes.AddSource(new CoreAotSource());

    public static void Configure(bool isServer)
    {
        lock (Lock) {
            _isConfigured = true;
            _isServer = isServer;
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
        // Server: StandardResolver as last-resort fallback — uses dynamic IL emit (JIT-only)
        //   to handle user types that aren't covered by the SG-generated resolvers. Safe on
        //   server because it's always JIT.
        // Client (Wasm / Maui / anywhere NativeAOT-bound): replace the dynamic fallback with
        //   static-only resolvers (BuiltinResolver for primitives/DateTime/Guid/etc.,
        //   AttributeFormatterResolver for [MessagePackFormatter]-marked types,
        //   ImmutableCollectionResolver for ImmutableArray/List/Dictionary). A type that
        //   isn't covered here or by the SG-generated resolvers throws FormatterNotRegistered
        //   at runtime — the intended behavior, since silently falling back to IL emit would
        //   hide AOT-unsafe paths.
        IFormatterResolver[] fallback = _isServer
            ? [StandardResolver.Instance]
            : [
                BuiltinResolver.Instance,
                AttributeFormatterResolver.Instance,
                MessagePack.ImmutableCollection.ImmutableCollectionResolver.Instance,
            ];

        var chain = new IFormatterResolver[PrependResolvers.Count + GeneratedResolvers.Count + fallback.Length];
        PrependResolvers.CopyTo(chain, 0);
        GeneratedResolvers.CopyTo(chain, PrependResolvers.Count);
        fallback.CopyTo(chain, PrependResolvers.Count + GeneratedResolvers.Count);
        DefaultMessagePackResolver.Resolvers = chain;
    }
}
