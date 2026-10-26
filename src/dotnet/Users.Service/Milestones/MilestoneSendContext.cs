namespace ActualChat.Users;

public sealed record MilestoneSendContext(
    UserMilestone Milestone,
    UserAttribution? Attribution,
    ApiArray<UserAnalyticsClient> Clients,
    Moment Now);
