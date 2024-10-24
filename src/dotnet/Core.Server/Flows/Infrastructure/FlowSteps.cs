namespace ActualChat.Flows.Infrastructure;

public static class FlowSteps
{
    public static readonly Symbol OnReset = nameof(OnReset);
    public static readonly Symbol OnHardResume = nameof(OnHardResume);
    public static readonly Symbol OnKill = nameof(OnKill);
    public static readonly Symbol OnEnding = nameof(OnEnding);
    public static readonly Symbol OnEnd = nameof(OnEnd);
    public static readonly Symbol OnMissingStep = nameof(OnMissingStep);

    // Special steps - you should never use them in your flows
    public static readonly Symbol Starting = nameof(Starting);
    public static readonly Symbol Removed = nameof(Removed); // Normally this Step shouldn't be used

    private static readonly MethodInfo ToUntypedMethod = typeof(FlowSteps)
        .GetMethod(nameof(ToUntyped), BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly ConcurrentDictionary<(Type, Symbol), Func<Flow, CancellationToken, Task>?>
        Cache = new();

    public static Func<Flow, CancellationToken, Task>? Get(
        Type flowType, Symbol step, bool useOnMissingStepFallback = false)
    {
        var stepFunc = Cache.GetOrAdd((flowType, step),
            static key => {
                var (flowType1, step1) = key;
                if (step1.IsEmpty)
                    throw new ArgumentOutOfRangeException(nameof(step));
                if (flowType1.IsGenericTypeDefinition)
                    throw new ArgumentOutOfRangeException(nameof(flowType));

                foreach (var method in flowType1.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)) {
                    if (!Equals(method.Name, step1.Value))
                        continue;
                    if (!typeof(Task).IsAssignableFrom(method.ReturnType))
                        continue;

                    var parameters = method.GetParameters();
                    if (parameters.Length != 1)
                        continue;
                    if (parameters[0].ParameterType != typeof(CancellationToken))
                        continue;

                    var fnType = typeof(Func<,,>)
                        .MakeGenericType(flowType1, typeof(CancellationToken), typeof(Task));
                    var untypedFn = method.CreateDelegate(fnType);
                    var fn = (Func<Flow, CancellationToken, Task>)ToUntypedMethod
                        .MakeGenericMethod(flowType1)
                        .Invoke(null, [untypedFn])!;
                    return fn;
                }
                return null;
            });
        if (stepFunc == null && useOnMissingStepFallback)
            stepFunc = Get(flowType, OnMissingStep);
        return stepFunc;
    }

    private static Func<Flow, CancellationToken, Task> ToUntyped<TFlow>(Delegate stepFn)
        where TFlow : Flow
        => (flow, cancellationToken) => {
            var typedFn = (Func<TFlow, CancellationToken, Task>)stepFn;
            return typedFn.Invoke((TFlow)flow, cancellationToken);
        };
}
