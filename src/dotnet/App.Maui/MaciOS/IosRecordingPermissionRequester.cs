using ActualChat.UI.Blazor.App.Services;
using Microsoft.Maui.ApplicationModel;

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
