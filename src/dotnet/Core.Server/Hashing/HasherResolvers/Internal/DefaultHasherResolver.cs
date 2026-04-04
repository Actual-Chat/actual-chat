namespace ActualChat.Hashing.Internal;

public sealed class DefaultHasherResolver : HasherResolver
{
    private static readonly ConcurrentDictionary<Type, Delegate?> Cache = new ();

    public static DefaultHasherResolver Instance { get; } = new();

    public override Delegate? Get(Type type)
        => Cache.GetOrAdd(type, static t => (Delegate?)Hashers.DefaultMethod.MakeGenericMethod(t).Invoke(null, []));
}
