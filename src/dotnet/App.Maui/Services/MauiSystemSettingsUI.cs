using System.Diagnostics.CodeAnalysis;
using ActualChat.UI;

namespace ActualChat.App.Maui.Services;

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
