using System.Collections.Frozen;

namespace ActualChat.Flows.Infrastructure;

public class FlowRegistry : IHasServices
{
    public IServiceProvider Services { get; }
    public IReadOnlyDictionary<Symbol, Type> TypeByName { get; }
    public IReadOnlyDictionary<Type, Symbol> NameByType { get; }

    public FlowRegistry(IServiceProvider services)
    {
        Services = services;
        var flowRegistryBuilder = services.GetRequiredService<FlowRegistryBuilder>();
        var flows = flowRegistryBuilder.Flows;
        TypeByName = flows.ToFrozenDictionary();
        NameByType = flows.ToFrozenDictionary(kv => kv.Value, kv => kv.Key);
    }

    public FlowId NewId<TFlow>(string arguments)
        where TFlow : Flow
        => new(NameByType[typeof(TFlow)], arguments);

    public FlowId NewId<TFlow>(params string[] arguments)
        where TFlow : Flow
        => new(NameByType[typeof(TFlow)], FlowId.CombineArguments(arguments));

    public FlowId NewId(Type flowType, string arguments)
        => new(NameByType[flowType], arguments);

    public FlowId NewId(Type flowType, params string[] arguments)
        => new(NameByType[flowType], FlowId.CombineArguments(arguments));
}
