using ActualChat.UI;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.App.Maui;

public class AppleRecordingPermissionRequester(AppUIHub hub) : IRecordingPermissionRequester
{
    private SystemSettingsUI SystemSettingsUI => field ??= hub.Services.GetRequiredService<SystemSettingsUI>();

    public bool CanRequest => true;

    public async Task<bool> TryRequest()
    {
        await SystemSettingsUI.Open(SystemSettingsSection.Microphone).ConfigureAwait(false);
        return true;
    }
}
