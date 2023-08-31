using ActualChat.Audio.UI.Blazor.Services;

namespace ActualChat.App.Maui;

public class IOSRecordingPermissionRequester : IRecordingPermissionRequester
{
    public Task<bool> TryRequest()
    {
        AppInfo.Current.ShowSettingsUI();
        return Stl.Async.TaskExt.TrueTask;
    }
}
