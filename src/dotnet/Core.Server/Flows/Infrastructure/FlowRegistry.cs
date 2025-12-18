using ActualLab.Internal;

namespace ActualChat.Flows.Infrastructure;

public class FlowRegistry
{
    private readonly Dictionary<Symbol, Type> _byName = new(64);
    private readonly Dictionary<Type, Symbol> _nameByType = new(64);

    public IReadOnlyDictionary<Symbol, Type> ByName => _byName;
    public bool UseMasterFlows { get; set; } = true;

    public FlowRegistry Add<TFlow>(Symbol name = default)
        where TFlow : Flow
        => Add(typeof(TFlow), name);

    public FlowRegistry Add(Type flowType, Symbol name = default)
    {
        RequireFlowType(flowType);
        if (_nameByType.ContainsKey(flowType))
            throw Errors.KeyAlreadyExists();

        if (name.IsEmpty)
            name = flowType.GetName();
        _byName.Add(name, flowType);
        _nameByType.Add(flowType, name);
        return this;
    }

    // RequireFlowType

    private static void RequireFlowType(Type type)
    {
        if (!typeof(Flow).IsAssignableFrom(type))
            throw Errors.MustBeAssignableTo<Flow>(type);
    }
}
