using System.Text.Json.Serialization.Metadata;

namespace ActualChat.Aot;

/// <summary>
/// Provides lists of types for AOT testing, assists in AOT type registration.
/// </summary>
public static class AotTypes
{
    private static readonly List<IAotSource> Sources = new();
#pragma warning disable CA1823 // Unused field
    private static readonly List<IJsonTypeInfoResolver> JsonTypeInfoResolverStore = new();
#pragma warning restore CA1823

    public static IDictionary<Type, AotTypeKind> All {
        get {
            if (field is not null) return field;
            lock (Sources)
                return field ??= Sources.SelectMany(x => x.ListTypes()).ToDictionary();
        }
    }

    public static void AddSource(IAotSource source)
    {
        if (CodeKeeper.AlwaysFalse)
            source.KeepTypes();
        lock (Sources) {
            // Deduplicate by source type
            var sourceType = source.GetType();
            if (Sources.Any(s => s.GetType() == sourceType))
                return;
            Sources.Add(source);
        }
    }
}
