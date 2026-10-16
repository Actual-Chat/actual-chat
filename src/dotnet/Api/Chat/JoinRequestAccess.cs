namespace ActualChat.Chat;

/// <summary>
/// Who, following a plain link to a private chat or Place, gets the page to request access;
/// anyone else gets the same "not found" as for a missing one. Ordered from the strictest.
/// </summary>
public enum JoinRequestAccess
{
    Off = 0,
    PlaceMembers = 1,
    SignedIn = 2,
    Anyone = 3,
}

public static class JoinRequestAccessExt
{
    public static bool Allows(this JoinRequestAccess access, bool isSignedIn, bool isPlaceMember)
        => access switch {
            JoinRequestAccess.Anyone => true,
            JoinRequestAccess.SignedIn => isSignedIn,
            JoinRequestAccess.PlaceMembers => isPlaceMember,
            _ => false,
        };

    public static JoinRequestAccess Min(this JoinRequestAccess access, JoinRequestAccess other)
        => access <= other ? access : other;
}
