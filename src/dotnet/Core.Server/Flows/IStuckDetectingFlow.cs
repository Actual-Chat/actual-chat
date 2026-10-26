using ActualChat.Flows.Infrastructure;

namespace ActualChat.Flows;

/// <summary>
/// A flow that tells by its own state whether it is stuck. Asked only about a flow whose type sets
/// <see cref="FlowAttribute.MaxResumeGap"/> and whose last resume is older than that; a flow that
/// doesn't implement it counts as stuck when no resume of it is pending.
/// </summary>
public interface IStuckDetectingFlow
{
    // nextResumeEvent is the earliest resume of this flow pending in the event log, if any;
    // an immediate resume already handed to the queue isn't there.
    bool IsStuck(FlowResumeEvent? nextResumeEvent);
}
