using System.Collections.Frozen;

namespace ActualChat;

public class BackendServiceDefs
{
    private readonly FrozenDictionary<Type, BackendServiceDef> _serviceDefs;

    public BackendServiceDef this[Type serviceType] => _serviceDefs[serviceType];

    public BackendServiceDefs(IServiceProvider services)
    {
        var serviceDefs = services.GetServices<BackendServiceDef>().ToList();
        _serviceDefs = serviceDefs.Select(x => KeyValuePair.Create(x.ServiceType, x))
            .Concat(serviceDefs.Select(x => KeyValuePair.Create(x.ImplementationType, x)))
            .DistinctBy(kv => kv.Key)
            .ToFrozenDictionary();
    }

    public bool Contains(Type serviceType)
        => _serviceDefs.ContainsKey(serviceType);

    public bool TryGet(Type serviceType, out BackendServiceDef? serviceDef)
        => _serviceDefs.TryGetValue(serviceType, out serviceDef);
}
