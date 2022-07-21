using System.Text.Encodings.Web;
using Cysharp.Text;

namespace ActualChat.UI.Blazor;

public static class NavigationManagerExt
{
    public static void Login(this NavigationManager navigator, string reason = "")
        => navigator.NavigateTo($"/login{MaybePathComponent(reason)}{MaybePathComponent(navigator.Uri)}");

    public static void Unavailable(this NavigationManager navigator, string what = "")
        => navigator.NavigateTo($"/unavailable{MaybePathComponent(what)}");

    public static void Chat(this NavigationManager navigator, string chatId = "")
        => navigator.NavigateTo(Links.ChatPage(chatId));

    // Private methods

    private static string UrlEncode(string? input)
        => UrlEncoder.Default.Encode(input ?? "");

    private static string MaybePathComponent(string? input)
    {
        if (input.IsNullOrEmpty())
            return "";
        return ZString.Concat('/' + UrlEncode(input));
    }
}
