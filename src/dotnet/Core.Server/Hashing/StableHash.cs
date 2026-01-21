using ActualLab.Caching;

namespace ActualChat.Hashing;

public static class StableHash
{
    private static readonly ConcurrentDictionary<Type, Delegate> Registered = new();
    private static readonly MethodInfo HashSymbolIdentifierMethod = typeof(StableHash)
        .GetMethod(nameof(HashSymbolIdentifier), BindingFlags.Static | BindingFlags.NonPublic)!;

    private static readonly HasherResolver DefaultResolver;
    public static HasherResolver Resolver { get; set; }

    static StableHash()
    {
        // Populate Registered
        Register<Unit>(_ => 0);
        Register<int>(x => x);
        Register<uint>(x => unchecked((int)x));
        Register<long>(x => unchecked((int)x));
        Register<ulong>(x => unchecked((int)x));
        Register<string?>(HashString);
        Register<Symbol>(value => HashString(value.Value));
        Register<IStringIdentifier?>(value => value is null ? 0 : HashString(value.Value));
        Register<IHasId<string>?>(value => value is null ? 0 : HashString(value.Id));
        Register<IHasId<Symbol>?>(value => value is null ? 0 : HashString(value.Id.Value));
        Register<Session?>(value => value is null ? 0 : HashString(value.Id));
        Register<ISessionCommand?>(value => value is null ? 0 : HashString(value.Session.Id));

        DefaultResolver = HasherResolver
            .New<Unit>(_ => 0)
            .Or<int>(x => x)
            .Or<uint>(x => unchecked((int)x))
            .Or<long>(x => unchecked((int)x))
            .Or<ulong>(x => unchecked((int)x))
            .Or<string?>(HashString)
            .Or<Symbol>(value => HashString(value.Value))
            .Or<IStringIdentifier?>(value => value is null ? 0 : HashString(value.Value))
            .Or(type => {
                if (!type.IsValueType)
                    return null;
                if (!typeof(ISymbolIdentifier).IsAssignableFrom(type))
                    return null;

#pragma warning disable IL2060
                return (Delegate?)HashSymbolIdentifierMethod
                    .MakeGenericMethod(type)
                    .CreateDelegate(typeof(Hasher<>).MakeGenericType(type));
#pragma warning restore IL2060
            })
            .Or<IHasId<string>?>(value => value is null ? 0 : HashString(value.Id))
            .Or<IHasId<Symbol>?>(value => value is null ? 0 : HashString(value.Id.Value))
            .Or<Session?>(value => value is null ? 0 : HashString(value.Id))
            .Or<ISessionCommand?>(value => value is null ? 0 : HashString(value.Session.Id))
            .Expanding()
            .Caching();

        Resolver = DefaultResolver;
    }

    public static void Register<T>(Hasher<T> hasher)
    {
        if (!Registered.TryAdd(typeof(T), hasher))
            throw StandardError.Internal($"Hasher for type {typeof(T).GetName()} is already registered.");
    }

    // Compute

    public static int Compute<T>(T value)
    {
        var hasher = Resolver == DefaultResolver
            ? Get<T>()
            : Resolver.Get<T>();
        return hasher?.Invoke(value) ?? value?.GetHashCode() ?? 0;
    }

    public static int Compute<T1, T2>(T1 value1, T2 value2)
        => Combine(Compute(value1), Compute(value2));

    public static int Compute<T1, T2, T3>(T1 value1, T2 value2, T3 value3)
        => Combine(Compute(value1), Compute(value2), Compute(value3));

    public static int Compute<T1, T2, T3, T4>(T1 value1, T2 value2, T3 value3, T4 value4)
        => Combine(Compute(value1), Compute(value2), Compute(value3), Compute(value4));

    // Combine

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Combine(int hash1, int hash2)
        => unchecked((353 * hash1) + hash2);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Combine(int hash1, int hash2, int hash3)
        => unchecked((353 * ((353 * hash1) + hash2)) + hash3);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Combine(params ReadOnlySpan<int> hashes)
    {
        if (hashes.IsEmpty)
            return 0;

        unchecked {
            int combined = hashes[0];
            for (int i = 1; i < hashes.Length; i++)
                combined = (353 * combined) + hashes[i];
            return combined;
        }
    }

    // Helper

    public static Hasher<T>? Get<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>()
        => GenericInstanceCache.GetUnsafe<Hasher<T>>(typeof(HasherFactory<>), typeof(T));

    // Private methods

    private static int HashString(string? value)
        => value?.GetXxHash3() ?? 0;

    private static int HashSymbolIdentifier<T>(T value)
        where T : ISymbolIdentifier
        => HashString(value.Value);

    // Nested types

    private sealed class HasherFactory<T> : GenericInstanceFactory, IGenericInstanceFactory<Hasher<T>?>
    {
        public override Hasher<T>? Generate()
        {
            var type = typeof(T);
            if (Registered.TryGetValue(type, out var registered))
                return (Hasher<T>)registered;

            if (type.IsValueType) {
                if (typeof(ISymbolIdentifier).IsAssignableFrom(type)) {
#pragma warning disable IL2060
                    return (Hasher<T>)HashSymbolIdentifierMethod
                        .MakeGenericMethod(type)
                        .CreateDelegate(typeof(Hasher<T>));
#pragma warning restore IL2060
                }
                return null;
            }

            foreach (var baseType in type.GetAllBaseTypes(false, true)) {
                if (Registered.TryGetValue(baseType, out registered))
                    return (Hasher<T>)Delegate.CreateDelegate(typeof(Hasher<T>), registered.Target, registered.Method);
            }
            return null;
        }
    }
}
