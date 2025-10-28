namespace ActualChat.Hashing.Internal;

public sealed class ChainHasherResolver(
    HasherResolver primaryResolver,
    HasherResolver secondaryResolver
) : HasherResolver
{
    public HasherResolver PrimaryResolver { get; } = primaryResolver;
    public HasherResolver SecondaryResolver { get; } = secondaryResolver;

    public override Delegate? Get(Type type)
        => PrimaryResolver.Get(type) ?? SecondaryResolver.Get(type);
}
