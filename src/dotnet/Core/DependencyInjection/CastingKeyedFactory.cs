namespace ActualChat.DependencyInjection;

public sealed class CastingKeyedFactory<TService, TKey, TFromService> : KeyedFactory<TService, TKey>
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
