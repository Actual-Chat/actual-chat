using Android.Content;

namespace ActualChat.App.Maui;

public static class AppLinksWorkaround
{
    // It seems App.OnAppLinkRequestReceived does not work in MAUI Hybrid Blazor.
    // https://github.com/dotnet/maui/issues/3788#issuecomment-1438888129
    // This is workaround adaptation to our intent handler.
    public static void HandleIntent(Intent? intent)
        => CheckForAppLink(intent);

    private static void CheckForAppLink(Intent? intent)
    {
        // A method to check if an application has been opened using a Universal link.
        // Android implementation.
        if (intent == null)
            return;

        var action = intent.Action;
        var link = intent.DataString.NullIfWhiteSpace();
        if (link == null || !OrdinalEquals(Intent.ActionView, action))
            return;

        App.Current.SendOnAppLinkRequestReceived(link.ToUri());
    }
}
