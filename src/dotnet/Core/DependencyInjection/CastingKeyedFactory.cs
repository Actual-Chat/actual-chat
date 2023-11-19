using System.Diagnostics.CodeAnalysis;

namespace ActualChat.DependencyInjection;

public sealed class CastingKeyedFactory<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TService,
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TKey,
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TFromService>
    : KeyedFactory<TService, TKey>
    where TService : class
    where TFromService : class
{
    public CastingKeyedFactory(KeyedFactory<TFromService, TKey> fromFactory)
        : base(fromFactory.Services, null)
    {
        var fromFactoryFactory = fromFactory.Factory;
        Factory = (c, key) => fromFactoryFactory.Invoke(c, key) as TService ?? throw new InvalidCastException();
    }
}
