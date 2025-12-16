using System.Collections.Frozen;

namespace ActualChat.Flows.Infrastructure;

public sealed class FlowRegistry
{
    public IReadOnlyDictionary<Symbol, Type> TypeByName { get; }
    public IReadOnlyDictionary<Type, Symbol> NameByType { get; }
    public IReadOnlyDictionary<Type, int> DataVersions { get; }
    public bool UseMasterFlows { get; }

    public FlowRegistry(IServiceProvider services)
    {
        var flowRegistryBuilder = services.GetRequiredService<FlowRegistryBuilder>();
        var flows = flowRegistryBuilder.Flows;
        TypeByName = flows.ToFrozenDictionary();
        NameByType = flows.ToFrozenDictionary(kv => kv.Value, kv => kv.Key);
        DataVersions = flows.ToDictionary(
            kv => kv.Value,
            kv => kv.Value.GetCustomAttribute<FlowAttribute>(inherit: true)?.DataVersion ?? 1);
        UseMasterFlows = flowRegistryBuilder.UseMasterFlows;
    }

    public FlowId NewId<TFlow>(string arguments)
        where TFlow : Flow
        => new(NameByType[typeof(TFlow)], arguments);

    public FlowId NewId<TFlow>(params ReadOnlySpan<string> arguments)
        where TFlow : Flow
        => new(NameByType[typeof(TFlow)], FlowId.CombineArguments(arguments));

    public FlowId NewId(Type flowType, string arguments)
        => new(NameByType[flowType], arguments);

    public FlowId NewId(Type flowType, params ReadOnlySpan<string> arguments)
        => new(NameByType[flowType], FlowId.CombineArguments(arguments));
}
