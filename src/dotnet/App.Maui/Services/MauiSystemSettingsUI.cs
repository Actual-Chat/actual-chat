using ActualChat.UI;

namespace ActualChat.App.Maui.Services;

public class MauiSystemSettingsUI : SystemSettingsUI
{
    public override Task Open()
    {
        AppInfo.Current.ShowSettingsUI();
        return Task.CompletedTask;
    }
}
