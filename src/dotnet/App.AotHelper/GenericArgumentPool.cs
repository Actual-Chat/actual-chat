namespace ActualChat.App.AotHelper;

/// <summary>
/// Supplies the generic arguments an open type has to be instantiated with to be worth compiling.
/// <para>
/// Reference-type instantiations share a single <c>__Canon</c> body, so every reference argument
/// collapses to <see cref="object"/>; value types each need their own body, so those are kept as
/// observed. That leaves exactly the instantiations R2R tells apart.
/// </para>
/// </summary>
public sealed class GenericArgumentPool
{
    private const int MaxTuplesPerDefinition = 256;

    private readonly Dictionary<Type, List<Type[]>> _observed = new();

    public static GenericArgumentPool Create(IEnumerable<Type> types)
    {
        var pool = new GenericArgumentPool();
        foreach (var type in types)
            pool.Observe(type);
        return pool;
    }

    public IEnumerable<Type> Instantiate(Type openType)
    {
        if (!openType.IsGenericType)
            return [];

        var definition = openType.IsGenericTypeDefinition ? openType : openType.GetGenericTypeDefinition();
        return GetArgumentTuples(definition)
            .Select(x => TryMake(definition, x))
            .Where(x => x != null)!;
    }

    // A canonical tuple is what R2R names, so two instantiations differing only in which reference
    // type they use are observed once.
    private void Observe(Type type)
    {
        if (!type.IsConstructedGenericType)
            return;

        var arguments = Canonicalize(type.GetGenericArguments());
        if (arguments == null)
            return;

        var definition = type.GetGenericTypeDefinition();
        if (!_observed.TryGetValue(definition, out var tuples))
            _observed[definition] = tuples = [];
        if (!tuples.Any(x => x.SequenceEqual(arguments)))
            tuples.Add(arguments);
    }

    // A nested type reports its parent's parameters as its own leading arguments, so the parent's
    // observed instantiations fix that prefix. What a method declares itself is left at object:
    // varying it over every value type we use multiplies the profile by ~3 to close ~14 methods.
    private IEnumerable<Type[]> GetArgumentTuples(Type definition)
    {
        var arity = definition.GetGenericArguments().Length;
        if (arity == 0)
            yield break;

        var declaring = definition.DeclaringType;
        var prefixArity = declaring is { IsGenericType: true } ? declaring.GetGenericArguments().Length : 0;
        if (prefixArity > arity)
            yield break;

        List<Type[]> prefixes;
        if (prefixArity == 0)
            prefixes = [[]];
        else if (_observed.TryGetValue(declaring!.GetGenericTypeDefinition(), out var observed) && observed.Count > 0)
            prefixes = observed;
        else
            prefixes = [Enumerable.Repeat(typeof(object), prefixArity).ToArray()];

        var suffix = Enumerable.Repeat(typeof(object), arity - prefixArity).ToArray();
        var count = 0;
        foreach (var prefix in prefixes) {
            if (++count > MaxTuplesPerDefinition)
                yield break;
            yield return [..prefix, ..suffix];
        }
    }

    private static Type[]? Canonicalize(Type[] types)
    {
        var result = new Type[types.Length];
        for (var i = 0; i < types.Length; i++) {
            if (Canonicalize(types[i]) is not { } canonical)
                return null;
            result[i] = canonical;
        }
        return result;
    }

    private static Type? Canonicalize(Type type)
    {
        if (type.ContainsGenericParameters || type.IsPointer || type.IsByRef)
            return null;
        if (!type.IsValueType)
            return typeof(object);
        if (!type.IsConstructedGenericType)
            return type;

        var arguments = Canonicalize(type.GetGenericArguments());
        return arguments == null ? null : TryMake(type.GetGenericTypeDefinition(), arguments);
    }

    private static Type? TryMake(Type definition, Type[] arguments)
    {
        try {
            return definition.MakeGenericType(arguments);
        }
        catch (Exception e) when (e is ArgumentException or TypeLoadException) {
            // Constraint violation - there is no such instantiation to keep.
            return null;
        }
    }
}
