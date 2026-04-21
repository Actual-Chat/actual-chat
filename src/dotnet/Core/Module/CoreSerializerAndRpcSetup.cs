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
        // SourceGeneratedFormatterResolver scans every loaded assembly for
        // [assembly: GeneratedAssemblyMessagePackResolver(typeof(X))] (emitted by the
        // MessagePack SG for ActualChat.Api / Api.Contracts / UI.Blazor.App as well as
        // external ActualLab.Rpc / Core / Fusion etc.) and dispatches to the matching
        // generated resolver — so we don't need to register each GeneratedMessagePackResolver
        // explicitly. It's cheap and touches no dynamic-emit machinery.
        //
        // StandardResolver is the JIT-only dynamic fallback for the long tail of user arrays,
        // enums, and other shapes the SG didn't pre-generate. Under genuine NativeAOT its
        // emit paths would throw — at which point AotFormatterPresenceTest is the regression
        // canary ensuring every type in AotTypes.All has a non-dynamic formatter registered
        // ahead of time.
        IFormatterResolver[] fallback = [
            SourceGeneratedFormatterResolver.Instance,
            StandardResolver.Instance,
        ];

        var chain = new IFormatterResolver[PrependResolvers.Count + fallback.Length];
        PrependResolvers.CopyTo(chain, 0);
        fallback.CopyTo(chain, PrependResolvers.Count);
        DefaultMessagePackResolver.Resolvers = chain;
    }
}
