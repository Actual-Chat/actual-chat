using ActualChat.App.Maui.Services;
using Activity = Android.App.Activity;

namespace ActualChat.App.Maui;

public class AndroidLivenessProbe : MauiLivenessProbe
{
    public static void Check(Activity activity)
        => Check();

    public static void AbortCheck(Activity activity)
        => CancelCheck();
}
