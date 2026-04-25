using MessagePack.Formatters;
using MessagePack.ImmutableCollection;
using MessagePack.Resolvers;

namespace ActualChat.Serialization.Internal;

/// <summary>
/// A composite MessagePack resolver that honors [MessagePackObject]/[DataContract] contracts
/// but ignores [Key(N)] - all members are serialized by name as a string-keyed map.
/// Uses <see cref="AppMessagePackResolverSettings"/> for the per-assembly plan mechanism.
/// </summary>
public sealed class AppMessagePackKeylessResolver : IFormatterResolver
{
    private static readonly ConcurrentDictionary<Assembly, AppMessagePackResolverAssemblyPlan> Plans = new();

    public static readonly IFormatterResolver Instance = new AppMessagePackKeylessResolver();
    public static readonly AppMessagePackResolverSettings Settings = new() {
        // DynamicForceStringKeyResolver runs before the SG/Object resolvers so App-assembly
        // [MessagePackObject] types serialize as string-keyed maps (ignoring [Key(N)]).
        // It refuses non-App types, so framework types (e.g. ActualLab.Rpc.RpcHandshake)
        // fall through to SourceGeneratedFormatterResolver / DynamicObjectResolver and keep
        // their native [Key(N)] array layout.
        Resolvers = [
            BuiltinResolver.Instance,
            AttributeFormatterResolver.Instance,
            ImmutableCollectionResolver.Instance,
            DynamicGenericResolver.Instance,
            DynamicUnionResolver.Instance,
            DynamicForceStringKeyResolver.Instance,
            SourceGeneratedFormatterResolver.Instance,
            DynamicObjectResolver.Instance,
        ],
    };

    private AppMessagePackKeylessResolver()
    { }

    public IMessagePackFormatter<T>? GetFormatter<T>()
        => Cache<T>.Formatter;

    // Private methods

    private static AppMessagePackResolverAssemblyPlan BuildAssemblyPlan(Assembly assembly)
        => Settings.BuildAssemblyPlan(assembly);

    // Nested types

    private static class Cache<T>
    {
        public static readonly IMessagePackFormatter<T>? Formatter = Build();

        private static IMessagePackFormatter<T>? Build()
        {
            var plan = Plans.GetOrAdd(typeof(T).Assembly, static a => BuildAssemblyPlan(a));
            if (plan.TryGetFormatter(typeof(T)) is { } custom)
                return Unsafe.As<IMessagePackFormatter<T>>(custom);

            foreach (var resolver in plan.Resolvers) {
                var f = resolver.GetFormatter<T>();
                if (f is not null)
                    return f;
            }
            return null;
        }
    }
}
