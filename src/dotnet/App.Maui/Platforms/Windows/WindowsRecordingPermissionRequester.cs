using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.App.Maui;

public class WindowsRecordingPermissionRequester : IRecordingPermissionRequester
{
    public bool CanRequest => true;

    public Task<bool> TryRequest()
    {
        AppInfo.Current.ShowSettingsUI();
        return ActualLab.Async.TaskExt.TrueTask;
    }
}
