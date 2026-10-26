namespace ActualChat.Users;

/// <summary>
/// A step a user reaches once, which predicts that they stay. A deeper repeat of the same action gets a kind
/// of its own, numbered by suffix: <c>Conversation</c>, <c>Conversation2</c>, ...
/// </summary>
public enum MilestoneKind
{
    // The first post outside the Notes chat
    Message = 0,
    // The first post in a 1:1 chat
    PeerChat = 1,
    // Another human posted among the chat's last entries too, so the two talk to each other
    Conversation = 2,
    // Posts on a second UTC day within 48 h of sign-up; the days need not be consecutive
    Day2Return = 3,
    Onboarded = 4,
}

public static class MilestoneKindExt
{
    public static string GetEventName(this MilestoneKind kind)
        => kind switch {
            MilestoneKind.Message => "message",
            MilestoneKind.PeerChat => "peer_chat",
            MilestoneKind.Conversation => "conversation",
            MilestoneKind.Day2Return => "day2_return",
            MilestoneKind.Onboarded => "onboarded",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    public static double? GetValue(this MilestoneKind kind)
        // About the chance, in percent, that a user who reached it is still active on days 60-90
        => kind switch {
            MilestoneKind.Message => 5,
            MilestoneKind.PeerChat => 8,
            MilestoneKind.Conversation => 22,
            MilestoneKind.Day2Return => 38,
            _ => null,
        };
}
