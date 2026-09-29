namespace ActualChat.UI.Blazor.Components;

public static class BubbleRegistry
{
    private static readonly ConcurrentDictionary<Type, string> TypeToId = new();
    private static readonly ConcurrentDictionary<string, Type> IdToType = new();

    public static string GetTypeId([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
        => TypeToId.GetOrAdd(type, static type1 => {
            if (!type1.IsAssignableTo(typeof(IBubble)))
                throw new ArgumentOutOfRangeException(nameof(type));

            // NOTE(AY): We intentionally use just type name here -
            // to make sure we can move them across namespaces w/o losing
            // read status.
            var typeId = type1.Name;
            IdToType.GetOrAdd(typeId, type1);
            return typeId;
        });

    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:RequiresUnreferencedCode",
        Justification = "UI types are expected to be untrimmed.")]
    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2072:UnrecognizedReflectionPattern",
        Justification = "UI types are expected to be untrimmed.")]
    public static string[] GetAllTypeIds()
    {
        // Every bubble type in the app, not only the ones rendered so far;
        // only assemblies referencing this one can declare them
        var ownAssembly = typeof(IBubble).Assembly;
        var ownAssemblyName = ownAssembly.GetName().Name;
        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(x => !x.IsDynamic
                && (x == ownAssembly || x.GetReferencedAssemblies().Any(r => r.Name == ownAssemblyName)))
            .SelectMany(x => x.GetTypes())
            .Where(x => x is { IsAbstract: false, IsInterface: false } && x.IsAssignableTo(typeof(IBubble)))
            .Select(GetTypeId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2073:ReturnValueDoesNotMatchAnnotation",
        Justification = "All possible results already have annotation.")]
    public static Type GetType(Symbol typeId)
        => IdToType.GetValueOrDefault(typeId)
            ?? throw new KeyNotFoundException(typeId);
}
