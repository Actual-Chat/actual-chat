using ActualChat.Chat.UI.Blazor.Components;
using Android;
using Android.Content;
using Android.Content.PM;

namespace ActualChat.App.Maui;

public class AndroidNoMicHandler : INoMicHandler
{
    public Task Allow()
    {
        var context = Platform.AppContext;
        var intent = new Intent();
        intent.SetAction(Android.Provider.Settings.ActionApplicationDetailsSettings);
        intent.AddCategory(Intent.CategoryDefault);
        intent.SetData(Android.Net.Uri.Parse("package:" + context.PackageName));
        intent.AddFlags(ActivityFlags.NewTask);
        intent.AddFlags(ActivityFlags.NoHistory);
        intent.AddFlags(ActivityFlags.ExcludeFromRecents);
        var task = TaskSource.New<Unit>(true).Task;

        void ActivityStateChanged(object? sender, ActivityStateChangedEventArgs args) {
            if (args.Activity is MainActivity && args.State == ActivityState.Resumed) {
                TaskSource.For(task).TrySetResult(Unit.Default);
                Platform.ActivityStateChanged -= ActivityStateChanged;
            }
        }

        Platform.ActivityStateChanged += ActivityStateChanged;
        context.StartActivity(intent);
        return task;
    }
}
