using ActualChat.Flows;

namespace ActualChat.Users.Flows;

/// <summary>
/// Delivers one user's milestones to the analytics and ad platforms. It is resumed when a milestone is reached,
/// an attribution or an analytics client id arrives, or the user consents to data collection, and by its retries.
/// </summary>
[Flow(DelayQuanta = 1, ResumeTimeout = 120)]
[DataContract, MessagePackObject(true)]
public sealed partial class MilestoneDeliveryFlow : Flow<Unit>
{
    private MilestoneDeliverer Deliverer => field ??= Services.GetRequiredService<MilestoneDeliverer>();
    private UserId UserId => field ??= UserId.Parse(Id.Arguments);

    // Protected/internal methods

    protected override async ValueTask Resume(CancellationToken cancellationToken)
    {
        var nextRunAt = await Deliverer.Deliver(UserId, cancellationToken).ConfigureAwait(false);
        if (nextRunAt is { } vNextRunAt)
            Runtime.StageResumeAt(vNextRunAt);
    }
}
