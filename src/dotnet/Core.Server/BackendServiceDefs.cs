using System.Collections.Frozen;

namespace ActualChat;

public sealed class BackendServiceDefs
{
    private readonly FrozenDictionary<Type, BackendServiceDef> _items;
    private string? _toStringCached;

    public BackendServiceDef this[Type serviceType] => _items[serviceType];

    public BackendServiceDefs(IServiceProvider services)
    {
        var items = services.GetServices<BackendServiceDef>().ToList();
        _items = items.Select(x => KeyValuePair.Create(x.ServiceType, x))
            .Concat(items.Select(x => KeyValuePair.Create(x.ImplementationType, x)))
            .DistinctBy(kv => kv.Key)
            .ToFrozenDictionary();
        var log = services.LogFor(GetType());
        log.LogInformation("{Description}", ToString());
    }

    public override string ToString()
    {
        if (_toStringCached != null)
            return _toStringCached;

        var items = _items.Values
            .Distinct()
            .Select(x => $"{Environment.NewLine}- {x}")
            .Order(StringComparer.Ordinal)
            .ToDelimitedString("");
        return _toStringCached = $"{nameof(BackendServiceDefs)}:" + items;
    }

    public bool Contains(Type serviceType)
        => _items.ContainsKey(serviceType);

    public bool TryGet(Type serviceType, out BackendServiceDef? serviceDef)
        => _items.TryGetValue(serviceType, out serviceDef);
}
