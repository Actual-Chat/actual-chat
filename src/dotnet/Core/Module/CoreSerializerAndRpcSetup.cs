using ActualChat.Aot;
using ActualLab.Rpc;
using ActualLab.Serialization.Internal;
using MessagePack.Resolvers;

namespace ActualChat.Module;

#pragma warning disable CA2255
#pragma warning disable CS0465 // 'Finalize' on a static class cannot interfere with destructor invocation

public static class CoreSerializerAndRpcSetup
{
    private static readonly Lock Lock = new();
    private static readonly List<IFormatterResolver> GeneratedResolvers = new();

    [ModuleInitializer]
    internal static void ModuleInitializer()
        => AotTypes.AddSource(new CoreAotSource());

    public static void Configure(bool isServer)
    {
        lock (Lock)
            RebuildResolverChain();
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
            = new(GetFullRpcSerializationFormat().Key);
#else
            = new((isServer ? GetFullRpcSerializationFormat() : GetCompactRpcSerializationFormat()).Key);
#endif
    }

    public static void AddGeneratedMessagePackResolver(IFormatterResolver resolver)
    {
        lock (Lock) {
            GeneratedResolvers.Add(resolver);
            RebuildResolverChain();
        }
    }

    // Private methods

    private static RpcSerializationFormat GetFullRpcSerializationFormat()
        => RpcSerializationFormat.MessagePackV6;

    private static RpcSerializationFormat GetCompactRpcSerializationFormat()
        => RpcSerializationFormat.MessagePackV6C;

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

        var chain = new IFormatterResolver[GeneratedResolvers.Count + fallback.Length];
        GeneratedResolvers.CopyTo(chain, 0);
        fallback.CopyTo(chain, GeneratedResolvers.Count);
        DefaultMessagePackResolver.Resolvers = chain;
    }
}
