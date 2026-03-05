namespace ActualChat.App.Maui.Services;

public static class NetworkAccessExt
{
    public static bool IsOnline(this NetworkAccess networkAccess)
        => networkAccess is not (NetworkAccess.None or NetworkAccess.Local);
}
