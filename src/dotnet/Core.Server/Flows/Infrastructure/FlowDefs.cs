using System.Collections.Frozen;
using System.Reflection.Emit;
using ActualChat.Queues;
using ActualChat.Time;

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
                    GetQuant = GetQuantTypeFactory(type),
                };
            }).ToList();
        ByType = flowDefs.ToFrozenDictionary(x => x.Type, x => x);
        ByName = flowDefs.ToFrozenDictionary(x => x.Name, x => x);
    }

    // Get methods

    public FlowDef Get<TFlow>() => ByType[typeof(TFlow)];
    public FlowDef Get(Type type) => ByType[type];
    public FlowDef Get(Symbol name) => ByName[name];

    // Private members

    private static Func<TimeSpan, TimeSpan>? GetQuantTypeFactory(Type flowType)
    {
        Type interfaceType = typeof(IQuantProvider);
        const string methodName = nameof(IQuantProvider.GetQuant);
        if (!interfaceType.IsAssignableFrom(flowType))
            return null;

        // Get the interface's MethodInfo
        var interfaceMethod = interfaceType.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public);
        if (interfaceMethod == null)
            return null;

        var genericMethod = typeof(FlowDefs)
            .GetMethod(nameof(GetQuantAccessor), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(flowType);

        var quantAccessor = (Func<Func<TimeSpan, TimeSpan>>)Delegate.CreateDelegate(typeof(Func<Func<TimeSpan, TimeSpan>>), genericMethod);
        return quantAccessor();
    }

    private static Func<TimeSpan,TimeSpan> GetQuantAccessor<T>() where T : IQuantProvider
        => T.GetQuant;
}
