namespace ActualChat.Users;

/// <summary>
/// A growth-funnel step counted in the <c>usage.funnel.events</c> OTLP counter.
/// </summary>
public enum FunnelEvent
{
    JoinOpenedSignedOut = 0,
    JoinOpenedSignedIn = 1,
    JoinUsed = 2,
    UserLinkOpenedSignedOut = 3,
    UserLinkOpenedSignedIn = 4,
    SignInRequestedFromLink = 5,
    SignInCompletedFromLink = 6,
    SignUp = 7,
    InviteBannerShown = 8,
    InviteShare = 9,
    InviteCopy = 10,
    InviteQr = 11,
    ContactsAccessGranted = 12,
    ContactsMatched = 13,
}

public static class FunnelEventExt
{
    public static bool IsClientReportable(this FunnelEvent funnelEvent)
        // Server-only events are counted where they happen; a client must not be able to inflate them
        => Enum.IsDefined(funnelEvent)
            && funnelEvent is not (FunnelEvent.JoinUsed or FunnelEvent.SignUp or FunnelEvent.ContactsMatched);
}
