using ActualChat.UI;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.App.Maui;

public class AppleRecordingPermissionRequester(SystemSettingsUI systemSettingsUI) : IRecordingPermissionRequester
{
    public bool CanRequest => true;

    public async Task<bool> TryRequest()
    {
        await systemSettingsUI.Open(SystemSettingsPane.Microphone).ConfigureAwait(false);
        return true;
    }
}
