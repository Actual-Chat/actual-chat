using ActualChat.UI.Blazor.Services;
using Android.Content;

namespace ActualChat.App.Maui;

public class AndroidHistoryExitHandler : IHistoryExitHandler
{
    public void Exit()
    {
        var currentActivity = Platform.CurrentActivity;
        if (currentActivity is MainActivity)
            MainThread.BeginInvokeOnMainThread(() => {
                if (currentActivity.IsTaskRoot)
                    currentActivity.MoveTaskToBack(true);
                else
                    Minimize(); // Minimizes the app on Android
            });
    }

    private static void Minimize()
    {
        var startMain = new Intent(Intent.ActionMain);
        startMain.AddCategory(Intent.CategoryHome);
        startMain.AddFlags(ActivityFlags.NewTask);
        Platform.AppContext.StartActivity(startMain);
    }
}
