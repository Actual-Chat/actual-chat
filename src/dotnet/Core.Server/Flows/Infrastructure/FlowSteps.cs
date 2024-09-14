namespace ActualChat.Flows.Infrastructure;

public static class FlowSteps
{
    public static readonly Symbol OnReset = nameof(OnReset);
    public static readonly Symbol OnExternalResume = nameof(OnExternalResume);
    public static readonly Symbol OnKill = nameof(OnKill);
    public static readonly Symbol OnEnding = nameof(OnEnding);
    public static readonly Symbol OnEnded = nameof(OnEnded);

    private static readonly MethodInfo ToUntypedMethod = typeof(FlowSteps)
        .GetMethod(nameof(ToUntyped), BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly ConcurrentDictionary<(Type, Symbol), Func<Flow, CancellationToken, Task<FlowTransition>>?>
        Cache = new();


    public static Func<Flow, CancellationToken, Task<FlowTransition>>? Get(Type flowType, Symbol step)
        => Cache.GetOrAdd((flowType, step), static key => {
            var (flowType1, step1) = key;
            if (step1.IsEmpty)
                throw new ArgumentOutOfRangeException(nameof(step));
            if (flowType1.IsGenericTypeDefinition)
                throw new ArgumentOutOfRangeException(nameof(flowType));

            foreach (var method in flowType1.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)) {
                if (!Equals(method.Name, step1.Value))
                    continue;
                if (method.ReturnType != typeof(Task<FlowTransition>))
                    continue;

                var parameters = method.GetParameters();
                if (parameters.Length != 1)
                    continue;
                if (parameters[0].ParameterType != typeof(CancellationToken))
                    continue;

                var fnType = typeof(Func<,,>)
                    .MakeGenericType(flowType1, typeof(CancellationToken), typeof(Task<FlowTransition>));
                var stepFn = method.CreateDelegate(fnType);
                var result = (Func<Flow, CancellationToken, Task<FlowTransition>>)ToUntypedMethod
                    .MakeGenericMethod(flowType1)
                    .Invoke(null, [stepFn])!;
                return result;
            }
            return null;
        });

    private static Func<Flow, CancellationToken, Task<FlowTransition>> ToUntyped<TFlow>(Delegate stepFn)
        where TFlow : Flow
        => (flow, cancellationToken) => {
            var typedFn = (Func<TFlow, CancellationToken, Task<FlowTransition>>)stepFn;
            return typedFn.Invoke((TFlow)flow, cancellationToken);
        };
}
