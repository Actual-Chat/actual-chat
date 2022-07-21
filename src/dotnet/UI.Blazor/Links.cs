namespace ActualChat.UI.Blazor;

public static class Links
{
    public static string ChatPage(string chatId)
        => "/chat/" + chatId.UrlEncode();

    public static string UserPage(string userId)
        => "/u/" + userId.UrlEncode();

    public static string SettingsPage()
        => "/settings";

    public static string InviteLink(string linkFormat, string inviteId)
        => String.Format(CultureInfo.InvariantCulture, linkFormat, inviteId.UrlEncode());
}
