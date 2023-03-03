namespace ActualChat.Users;

public static class SessionInfoExt
{
    public static UserId GetGuestId(this SessionInfo? sessionInfo)
    {
        if (sessionInfo == null)
            return default;

        GuestIdOption? guestIdOption = null;
        try {
            guestIdOption = sessionInfo.Options.Get<GuestIdOption>();
        }
        catch {
            // Intended: GuestId type was changed, so it might throw an error
        }
        var guestId = guestIdOption?.GuestId ?? default;
        return guestId.IsGuest ? guestId : default;
    }
}
