using System.Collections.Frozen;
using ActualChat.Reflection;
using ActualLab.Api.Internal;
using ActualLab.Collections.Internal;
using ActualLab.Serialization.Internal;
using Cysharp.Serialization.MessagePack;
using MessagePack.ImmutableCollection;
using MessagePack.Resolvers;

namespace ActualChat.Serialization.Internal;

#pragma warning disable CA2227 // Make property read-only

public sealed record AppMessagePackResolverSettings
{
    public static Dictionary<Type, Type> Formatters { get; set; } = new() {
        { typeof(Unit), typeof(UnitMessagePackFormatter) },
        { typeof(Ulid), typeof(UlidMessagePackFormatter) },
        { typeof(Option<>), typeof(OptionMessagePackFormatter<>) },
        { typeof(ApiOption<>), typeof(ApiOptionMessagePackFormatter<>) },
        { typeof(ApiNullable<>), typeof(ApiNullableMessagePackFormatter<>) },
        { typeof(ApiNullable8<>), typeof(ApiNullable8MessagePackFormatter<>) },
        { typeof(ApiArray<>), typeof(ApiArrayMessagePackFormatter<>) },
        { typeof(Box<>), typeof(BoxMessagePackFormatter<>) },
        { typeof(MutableBox<>), typeof(MutableBoxMessagePackFormatter<>) },
        { typeof(Result<>), typeof(ResultMessagePackFormatter<>) },
        { typeof(ImmutableBimap<,>), typeof(ImmutableBimapMessagePackFormatter<,>) },
    };

    public static IReadOnlyList<IFormatterResolver> StandardResolvers { get; }

    public static Func<Assembly, AssemblyKind, IFormatterResolver, bool> ResolverFilter { get; set; }
        = DefaultResolverFilter;

    private static readonly LazySlim<HashSet<Assembly>> FormatterAssembliesLazy;

    public IReadOnlyList<IFormatterResolver> Resolvers { get; init; } = StandardResolvers;
    public IReadOnlyDictionary<Type, Type> ExtraFormatters { get; init; } = ImmutableDictionary<Type, Type>.Empty;

    static AppMessagePackResolverSettings()
    {
        FormatterAssembliesLazy = new (ComputeFormatterAssemblies);
        var standardResolvers = new List<IFormatterResolver>() {
            BuiltinResolver.Instance,
            AttributeFormatterResolver.Instance,
            SourceGeneratedFormatterResolver.Instance,
            ImmutableCollectionResolver.Instance,
            DynamicGenericResolver.Instance,
        };
        if (RuntimeFeature.IsDynamicCodeSupported) {
            standardResolvers.Add(DynamicUnionResolver.Instance);
            standardResolvers.Add(DynamicObjectResolver.Instance);
        }
        StandardResolvers = standardResolvers.ToArray();
    }

    public static bool DefaultResolverFilter(Assembly a, AssemblyKind kind, IFormatterResolver resolver)
        => kind switch {
            AssemblyKind.System => resolver is BuiltinResolver or ImmutableCollectionResolver or DynamicGenericResolver,
            _ => resolver is not BuiltinResolver,
        };

    public AppMessagePackResolverAssemblyPlan BuildAssemblyPlan(Assembly assembly)
    {
        var d = new Dictionary<Type, Type>();
        foreach (var kv in Formatters)
            if (kv.Key.Assembly == assembly)
                d[kv.Key] = kv.Value;
        foreach (var kv in ExtraFormatters)
            if (kv.Key.Assembly == assembly)
                d[kv.Key] = kv.Value;
        var localFormatters = d.Count > 0 ? d.ToFrozenDictionary() : null;

        var resolvers = ArrayBuffer<IFormatterResolver>.Lease(true, Resolvers.Count);
        try {
            var kind = assembly.GetKind();
            foreach (var r in Resolvers)
                if (ResolverFilter.Invoke(assembly, kind, r))
                    resolvers.Add(r);
            return new AppMessagePackResolverAssemblyPlan(localFormatters, resolvers.ToArray());
        }
        finally {
            resolvers.Release();
        }
    }

    // Private methods

    private static HashSet<Assembly> ComputeFormatterAssemblies()
    {
        var result = new HashSet<Assembly>();
        foreach (var kv in Formatters)
            result.Add(kv.Key.Assembly);
        return result;
    }
}
