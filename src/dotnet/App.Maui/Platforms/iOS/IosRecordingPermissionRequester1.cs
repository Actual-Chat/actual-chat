using ActualChat.Audio.UI.Blazor.Services;

namespace ActualChat.App.Maui;

public class IosRecordingPermissionRequester : IRecordingPermissionRequester
{
    public bool CanRequest => true;

    public Task<bool> TryRequest()
    {
        AppInfo.Current.ShowSettingsUI();
        return ActualLab.Async.TaskExt.TrueTask;
    }
}
