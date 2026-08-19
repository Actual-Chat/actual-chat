using ActualChat.Flows;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

// Carries a DelayQuanta so its resume events reach the quantized-Uuid branch of
// FlowResumeEvent.ToOperationEvent - TimerFlow declares none and never gets there.

[Flow(ResumeTimeout = 60, DelayQuanta = 30)]
[DataContract, MessagePackObject(true)]
public sealed partial class QuantaFlow : Flow<Unit>
{
    protected override ValueTask Resume(CancellationToken cancellationToken)
    {
        SetResult(default);
        return default;
    }
}
