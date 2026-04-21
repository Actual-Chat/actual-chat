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
        // SourceGeneratedFormatterResolver picks up [assembly: GeneratedAssemblyMessagePackResolver]
        // from external assemblies (ActualLab.Rpc / Core / Fusion, etc.), routed by fully-
        // qualified type name. ActualChat's own Api / Api.Contracts / UI.Blazor.App resolvers
        // still need explicit AddGeneratedResolver (they share the same
        // MessagePack.GeneratedMessagePackResolver name so only one would be auto-discovered).
        //
        // StandardResolver is the JIT-only dynamic fallback for the long tail of user arrays,
        // enums, and other shapes the SG didn't pre-generate. Under genuine NativeAOT its
        // emit paths would throw — at which point AotFormatterPresenceTest is the regression
        // canary ensuring every type in AotTypes.All has a non-dynamic formatter registered
        // ahead of time. On Wasm JIT client it just works.
        IFormatterResolver[] fallback = [
            SourceGeneratedFormatterResolver.Instance,
            StandardResolver.Instance,
        ];

        var chain = new IFormatterResolver[PrependResolvers.Count + GeneratedResolvers.Count + fallback.Length];
        PrependResolvers.CopyTo(chain, 0);
        GeneratedResolvers.CopyTo(chain, PrependResolvers.Count);
        fallback.CopyTo(chain, PrependResolvers.Count + GeneratedResolvers.Count);
        DefaultMessagePackResolver.Resolvers = chain;
    }
}
