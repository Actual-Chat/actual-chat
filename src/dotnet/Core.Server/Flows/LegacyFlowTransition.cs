using ActualLab.CommandR.Operations;

namespace ActualChat.Flows;

[StructLayout(LayoutKind.Auto)]
public readonly record struct LegacyFlowTransition : ICanBeNone<LegacyFlowTransition>
{
    public static LegacyFlowTransition None => default;

    public LegacyFlow Flow { get; init; }
    public Symbol Step { get; init; }
    public string? Tag { get; init; } = null;
    public bool MustStore { get; init; }
    public Moment? HardResumeAt { get; init; }
    public ImmutableList<OperationEvent> Events { get; init; } = ImmutableList<OperationEvent>.Empty;

    public bool IsNone
        => ReferenceEquals(Flow, null);
    public bool EffectiveMustStore
        => MustStore || Events.Count != 0;

    // ReSharper disable once ConvertToPrimaryConstructor
    public LegacyFlowTransition(LegacyFlow flow, Symbol step, string? tag)
    {
        Flow = flow;
        Step = step;
        Tag = tag;
    }

    // ReSharper disable once ConvertToPrimaryConstructor
    public LegacyFlowTransition(LegacyFlow flow, Symbol step, string? tag, OperationEvent @event)
    {
        Flow = flow;
        Step = step;
        Tag = tag;
        Events = Events.Add(@event);
    }

    // ReSharper disable once ConvertToPrimaryConstructor
    public LegacyFlowTransition(LegacyFlow flow, Symbol step, string? tag, Moment hardResumeAt)
    {
        Flow = flow;
        Step = step;
        Tag = tag;
        HardResumeAt = hardResumeAt;
    }

    // ReSharper disable once ConvertToPrimaryConstructor
    public LegacyFlowTransition(LegacyFlow flow, Symbol step, string? tag, Moment hardResumeAt, OperationEvent @event)
    {
        Flow = flow;
        Step = step;
        Tag = tag;
        HardResumeAt = hardResumeAt;
        Events = Events.Add(@event);
    }

    // ReSharper disable once ConvertToPrimaryConstructor
    public LegacyFlowTransition(LegacyFlow flow, Symbol step, string? tag, Moment hardResumeAt, OperationEvent[] events)
    {
        Flow = flow;
        Step = step;
        Tag = tag;
        HardResumeAt = hardResumeAt;
        if (events.Length != 0)
            Events = Events.AddRange(events);
    }

    public override string ToString()
    {
        if (IsNone)
            return $"{nameof(LegacyFlowTransition)}.{nameof(None)}";

        var sTag = Tag.IsNullOrEmpty() ? "" : $", '{Tag}'";
        var sMustStore = EffectiveMustStore ? ", store" : ", no-store";
        var sResumesIn = ", resumes now";
        if (HardResumeAt is { } hardResumeAt) {
            sResumesIn = hardResumeAt >= LegacyFlow.InfiniteHardResumeAt
                ? ", never resumes"
                : $", hard resumes in {(hardResumeAt - SystemClock.Instance.Now).ToShortString()}";
        }
        var hasEvents = Events.Count != 0;
        var sEvents = hasEvents ? $", {Events.Count} {"event".Pluralize(Events.Count)}" : "";
        return $"{nameof(LegacyFlowTransition)}(->{Step}{sTag}{sMustStore}{sResumesIn}{sEvents})";
    }
}
