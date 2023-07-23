using Android.Content;
using Android.OS;
using Microsoft.Maui.LifecycleEvents;

namespace ActualChat.App.Maui;

public static class AppLinksWorkaround
{
    public static void ConfigureAndroidLifecycleEvents(IAndroidLifecycleBuilder android)
    {
        // It seems App.OnAppLinkRequestReceived does not work in MAUI Hybrid Blazor.
        // https://github.com/dotnet/maui/issues/3788#issuecomment-1438888129
        // This is workaround.
        android.OnNewIntent(OnNewIntent);
        android.OnCreate(OnCreate);
    }

    private static void OnCreate(Android.App.Activity activity, Bundle? savedInstanceState)
        => CheckForAppLink(activity.Intent);

    private static void OnNewIntent(Android.App.Activity activity, Intent? intent)
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
