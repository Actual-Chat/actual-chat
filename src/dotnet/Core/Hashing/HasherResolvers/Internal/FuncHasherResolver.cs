namespace ActualChat.Hashing.Internal;

public sealed class FuncHasherResolver(Func<Type, Delegate?> resolver) : HasherResolver
{
    public Func<Type, Delegate?> Resolver { get; } = resolver;

    public override Delegate? Get(Type type)
        => Resolver.Invoke(type);
}
