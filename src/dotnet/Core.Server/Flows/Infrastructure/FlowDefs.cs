using System.Collections.Frozen;
using ActualChat.Flows.Internal;
using ActualChat.Queues;

namespace ActualChat.Flows.Infrastructure;

public sealed class FlowDefs
{
    public IReadOnlyDictionary<Type, FlowDef> ByType { get; }
    public IReadOnlyDictionary<Symbol, FlowDef> ByName { get; }

    public FlowDefs(IServiceProvider services)
    {
        var flowDefs = services.GetRequiredService<FlowRegistry>().ByName
            .Select(kv => {
                var (name, type) = kv;
                var attr = type.GetCustomAttribute<FlowAttribute>(inherit: true);
                return new FlowDef(type, name) {
                    DataVersion = attr?.DataVersion ?? 1,
                    ResumeTimeout = attr?.GetResumeTimeoutAsTimeSpan() ?? QueuesExt.DefaultTimeout,
                    DelayQuanta = attr?.GetDelayQuantaAsTimeSpan(),
                };
            }).ToList();
        ByType = flowDefs.ToFrozenDictionary(x => x.Type, x => x);
        ByName = flowDefs.ToFrozenDictionary(x => x.Name, x => x);
    }

    // Get methods

    public FlowDef Get<TFlow>() => Get(typeof(TFlow));

    public FlowDef Get(Type type)
        => ByType.TryGetValue(type, out var flowDef)
            ? flowDef
            : throw Errors.UnknownFlow(type);

    public FlowDef Get(Symbol name)
        => ByName.TryGetValue(name, out var flowDef)
            ? flowDef
            : throw Errors.UnknownFlow(name);
}
