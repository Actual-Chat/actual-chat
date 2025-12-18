using System.Collections.Frozen;
using ActualChat.Queues;

namespace ActualChat.Flows.Infrastructure;

public sealed class FlowDefs
{
    public IReadOnlyDictionary<Type, FlowDef> ByType { get; }
    public IReadOnlyDictionary<Symbol, FlowDef> ByName { get; }

    public FlowDefs(IServiceProvider services)
    {
        var flowRegistryBuilder = services.GetRequiredService<FlowDefsBuilder>();
        var flowDefs = flowRegistryBuilder.Flows
            .Select(kv => {
                var (name, type) = kv;
                var attr = type.GetCustomAttribute<FlowAttribute>(inherit: true);
                return new FlowDef(type, name) {
                    DataVersion = attr?.DataVersion ?? 1,
                    ResumeTimeout = attr?.GetResumeTimeoutAsTimeSpan() ?? QueuesExt.DefaultTimeout,
                };
            }).ToList();
        ByType = flowDefs.ToFrozenDictionary(x => x.Type, x => x);
        ByName = flowDefs.ToFrozenDictionary(x => x.Name, x => x);
    }

    // Get methods

    public FlowDef Get<TFlow>() => ByType[typeof(TFlow)];
    public FlowDef Get(Type type) => ByType[type];
    public FlowDef Get(Symbol name) => ByName[name];
}
