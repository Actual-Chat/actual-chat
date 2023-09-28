namespace ActualChat.UI.Blazor.Diagnostics;

internal sealed class ConditionalPropagator : DistributedContextPropagator
{
    public static readonly AsyncLocal<bool> IgnoreRequest = new();

    private readonly DistributedContextPropagator _originalPropagator = Current;

    public override IReadOnlyCollection<string> Fields => _originalPropagator.Fields;

    public override void Inject(Activity? activity, object? carrier, PropagatorSetterCallback? setter)
    {
        if (IgnoreRequest.Value)
            return;
        _originalPropagator.Inject(activity, carrier, setter);
    }

    public override void ExtractTraceIdAndState(object? carrier, PropagatorGetterCallback? getter, out string? traceId, out string? traceState) =>
        _originalPropagator.ExtractTraceIdAndState(carrier, getter, out traceId, out traceState);

    public override IEnumerable<KeyValuePair<string, string?>>? ExtractBaggage(object? carrier, PropagatorGetterCallback? getter) =>
        _originalPropagator.ExtractBaggage(carrier, getter);
}
