namespace ActualChat.App.AotHelper;

/// <summary>
/// Resolves types the keepers name but that can't be written as <c>typeof(…)</c> here — most are
/// nested or internal to ActualLab. Anything that fails to resolve is recorded, so a rename in
/// ActualLab surfaces as a reported miss instead of a silently smaller profile.
/// </summary>
public static class TypeResolver
{
    private static readonly Dictionary<string, Type?> Cache = new(StringComparer.Ordinal);

    public static Type? Resolve(string fullName, ICollection<string> unresolved)
        => Cache.TryGetValue(fullName, out var cached)
            ? cached
            : Cache[fullName] = Find(x => x.GetType(fullName, throwOnError: false), fullName, unresolved);

    public static Type? ResolveBySuffix(string nameSuffix, ICollection<string> unresolved)
    {
        if (Cache.TryGetValue(nameSuffix, out var cached))
            return cached;

        var suffix = "." + nameSuffix;
        return Cache[nameSuffix] = Find(
            assembly => assembly.GetTypes().FirstOrDefault(
                x => x.FullName is { } name && name.EndsWith(suffix, StringComparison.Ordinal)),
            nameSuffix,
            unresolved);
    }

    public static void TryInstantiate(ISet<Type> types, Type definition, Type argument)
    {
        var arity = definition.GetGenericArguments().Length;
        if (arity != 1)
            return;

        try {
            types.Add(definition.MakeGenericType(argument));
        }
        catch (Exception e) when (e is ArgumentException or TypeLoadException) {
            // Constraint violation - there is no such instantiation to keep.
        }
    }

    public static void TryInstantiate(ICollection<MethodBase> methods, MethodInfo definition, Type argument)
    {
        try {
            methods.Add(definition.MakeGenericMethod(argument));
        }
        catch (Exception e) when (e is ArgumentException or TypeLoadException) {
            // Constraint violation - there is no such instantiation to keep.
        }
    }

    private static Type? Find(Func<Assembly, Type?> selector, string name, ICollection<string> unresolved)
    {
        foreach (var assembly in Ordered(AppDomain.CurrentDomain.GetAssemblies())) {
            try {
                if (selector(assembly) is { } type)
                    return type;
            }
            catch (ReflectionTypeLoadException) { }
        }
        unresolved.Add(name);
        return null;
    }

    private static IEnumerable<Assembly> Ordered(Assembly[] assemblies)
        => assemblies
            .OrderByDescending(x => x.GetName().Name?.StartsWith("ActualLab.", StringComparison.Ordinal) == true)
            .ThenByDescending(x => x.GetName().Name?.StartsWith("ActualChat.", StringComparison.Ordinal) == true);
}
