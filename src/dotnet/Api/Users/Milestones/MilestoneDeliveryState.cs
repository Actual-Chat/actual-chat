namespace ActualChat.Users;

public enum MilestoneDeliveryState
{
    AwaitingConsent = 0,
    Pending = 1,
    Sent = 2,
    // Treated as delivered: the target must never get it, e.g. there is no attribution to credit it to
    Skipped = 3,
    Failed = 4,
}

public static class MilestoneDeliveryStateExt
{
    public static bool IsFinal(this MilestoneDeliveryState state)
        => state is MilestoneDeliveryState.Sent or MilestoneDeliveryState.Skipped or MilestoneDeliveryState.Failed;
}
