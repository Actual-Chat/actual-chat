using ActualLab.Internal;

namespace ActualChat.Flows.Infrastructure;

public class FlowRegistryBuilder
{
    private readonly Dictionary<Symbol, Type> _flows = new(64);
    private readonly Dictionary<Type, Symbol> _flowNameByType = new(64);

    public IReadOnlyDictionary<Symbol, Type> Flows => _flows;

    public FlowRegistryBuilder Add<TFlow>(Symbol name = default)
        where TFlow : Flow
        => Add(typeof(TFlow), name);

    public FlowRegistryBuilder Add(Type flowType, Symbol name = default)
    {
        Flow.RequireCorrectType(flowType);
        if (_flowNameByType.ContainsKey(flowType))
            throw Errors.KeyAlreadyExists();

        if (name.IsEmpty)
            name = flowType.GetName();
        _flows.Add(name, flowType);
        _flowNameByType.Add(flowType, name);
        return this;
    }
}
