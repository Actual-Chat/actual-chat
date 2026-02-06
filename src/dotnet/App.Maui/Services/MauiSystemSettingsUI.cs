using ActualChat.UI;

namespace ActualChat.App.Maui.Services;

/// <summary>
/// MAUI implementation of <see cref="SystemSettingsUI"/> that opens platform system settings.
/// </summary>
public class MauiSystemSettingsUI : SystemSettingsUI
{
    [method: DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MauiSystemSettingsUI))]
    public MauiSystemSettingsUI() { }

    public override Task Open()
    {
        AppInfo.Current.ShowSettingsUI();
        return Task.CompletedTask;
    }
}
