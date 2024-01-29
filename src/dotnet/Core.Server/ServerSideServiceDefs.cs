using System.Collections.Frozen;

namespace ActualChat;

public class ServerSideServiceDefs
{
    private readonly FrozenDictionary<Type, ServerSideServiceDef> _serviceDefs;

    public ServerSideServiceDef this[Type serviceType] => _serviceDefs[serviceType];

    public ServerSideServiceDefs(IServiceProvider services)
    {
        var serviceDefs = services.GetService<IEnumerable<ServerSideServiceDef>>()?.ToList() ?? new();
        _serviceDefs = serviceDefs.Select(x => KeyValuePair.Create(x.ServiceType, x))
            .Concat(serviceDefs.Select(x => KeyValuePair.Create(x.ImplementationType, x)))
            .DistinctBy(kv => kv.Key)
            .ToFrozenDictionary();
    }

    public bool TryGet(Type serviceType, out ServerSideServiceDef? serviceDef)
        => _serviceDefs.TryGetValue(serviceType, out serviceDef);
}
