using ActualChat.Audio.UI.Blazor.Services;
using Android.Content;

namespace ActualChat.App.Maui;

public class AndroidRecordingPermissionRequester : IRecordingPermissionRequester
{
    public Task<bool> TryRequest()
    {
        var context = Platform.AppContext;
        var intent = new Intent();
        intent.SetAction(Android.Provider.Settings.ActionApplicationDetailsSettings);
        intent.AddCategory(Intent.CategoryDefault);
        intent.SetData(Android.Net.Uri.Parse("package:" + context.PackageName));
        intent.AddFlags(ActivityFlags.NewTask);
        intent.AddFlags(ActivityFlags.NoHistory);
        intent.AddFlags(ActivityFlags.ExcludeFromRecents);
        var resultSource = TaskCompletionSourceExt.New<bool>();

        void ActivityStateChanged(object? sender, ActivityStateChangedEventArgs args) {
            if (args is { Activity: MainActivity, State: ActivityState.Resumed }) {
                resultSource.TrySetResult(true);
                Platform.ActivityStateChanged -= ActivityStateChanged;
            }
        }

        Platform.ActivityStateChanged += ActivityStateChanged;
        context.StartActivity(intent);
        return resultSource.Task;
    }
}
