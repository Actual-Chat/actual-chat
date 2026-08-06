using System.Collections.Frozen;
using MessagePack.Formatters;

namespace ActualChat.Serialization.Internal;

/// <summary>
/// A drop-in replacement for the default MessagePack resolver that accelerates
/// <c>GetFormatter&lt;T&gt;</c> by pre-computing a per-assembly resolution plan:
/// <list type="bullet">
///   <item>System assemblies (System.*, Microsoft.*, corelib, netstandard) - minimal set
///         (Builtin, Immutable, Generic). Attribute / Object resolvers are pointless there.</item>
///   <item>"Source" assemblies - those that define entries in <see cref="Formatters"/> -
///         get a per-assembly subset of <see cref="Formatters"/> (a <see cref="FrozenDictionary{TKey,TValue}"/>
///         on net8+) plus all resolvers except Builtin.</item>
///   <item>Other assemblies - all resolvers except Builtin.</item>
/// </list>
/// The plan for each assembly is built lazily on first <c>GetFormatter</c> for a type in it,
/// cached in a <see cref="ConcurrentDictionary{TKey,TValue}"/>, and then reused forever.
/// Per-T formatter lookup is still cached in a generic static (one build per closed T).
/// </summary>
/// <remarks>
/// Changes to <see cref="Formatters"/>, <see cref="Resolvers"/> or <see cref="ResolverFilter"/>
/// after the first use are NOT observed by already-built plans or the per-T cache.
/// Configure before first serialization.
/// </remarks>
public class AppMessagePackResolver : IFormatterResolver
{
    private static readonly ConcurrentDictionary<Assembly, AppMessagePackResolverAssemblyPlan> Plans = new();

    public static readonly IFormatterResolver Instance = new AppMessagePackResolver();
    public static readonly AppMessagePackResolverSettings Settings = new() {
        // Size2D is registered here rather than via [MessagePackFormatter] on the type itself,
        // because AttributeFormatterResolver would then win in AppMessagePackKeylessResolver too
        // and turn VideoFormat.Size into an array - the TS clients (msgpack6ck) expect a map.
        ExtraFormatters = new Dictionary<Type, Type> {
            { typeof(Size2D), typeof(Size2DMessagePackFormatter) },
        },
    };

    private AppMessagePackResolver()
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
