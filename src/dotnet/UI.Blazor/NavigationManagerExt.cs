using System.Text.Encodings.Web;

namespace ActualChat.UI.Blazor;

public static class NavigationManagerExt
{
    // Private methods

    private static string UrlEncode(string? input)
        => UrlEncoder.Default.Encode(input ?? "");
}
