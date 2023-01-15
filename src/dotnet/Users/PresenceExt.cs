namespace ActualChat.Users;

public static class PresenceExt
{
    public static bool IsOnline(this Presence presence)
        => presence is Presence.Online or Presence.Recording;
}
