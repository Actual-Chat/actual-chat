using System.Collections.Frozen;

namespace ActualChat;

public class BackendServiceDefs
{
    private readonly FrozenDictionary<Type, BackendServiceDef> _items;

    public BackendServiceDef this[Type serviceType] => _items[serviceType];

    public BackendServiceDefs(IServiceProvider services)
    {
        var items = services.GetServices<BackendServiceDef>().ToList();
        _items = items.Select(x => KeyValuePair.Create(x.ServiceType, x))
            .Concat(items.Select(x => KeyValuePair.Create(x.ImplementationType, x)))
            .DistinctBy(kv => kv.Key)
            .ToFrozenDictionary();
    }

    public bool Contains(Type serviceType)
        => _items.ContainsKey(serviceType);

    public bool TryGet(Type serviceType, out BackendServiceDef? serviceDef)
        => _items.TryGetValue(serviceType, out serviceDef);
}
