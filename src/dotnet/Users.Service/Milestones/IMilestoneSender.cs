namespace ActualChat.Users;

/// <summary>
/// Sends milestones to one <see cref="MilestoneTarget"/>, and decides which ones that target takes:
/// each platform has its own windows and requirements.
/// </summary>
public interface IMilestoneSender
{
    MilestoneTarget Target { get; }
    bool IsEnabled { get; }

    MilestoneCheck Check(MilestoneSendContext context);
    Task<MilestoneSendOutcome> Send(MilestoneSendContext context, CancellationToken cancellationToken);
}
