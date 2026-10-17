using ActualChat.Localization;
using ActualChat.UI.Blazor.App.Services;
using Microsoft.Extensions.Localization;
using ServiceManagement;

namespace ActualChat.App.Maui;

public class MacOSAppSettings(IStringLocalizer l) : INativeAppSettings
{
    private static readonly ILogger Log = StaticLog.For<MacOSAppSettings>();

    public Task<AutoStartState> GetAutoStartState()
    {
        var result = SMAppService.MainApp.Status switch {
            SMAppServiceStatus.Enabled => new AutoStartState(true),
            // Registered, but the user turned it off in Login Items - only they can turn it back on there
            SMAppServiceStatus.RequiresApproval => new AutoStartState(false, false,
                l.NativeApp_AutoStartRequiresApproval_Format(CoreConstants.AppName),
                SMAppService.OpenSystemSettingsLoginItems),
            SMAppServiceStatus.NotFound => new AutoStartState(false, false, l.NativeApp_AutoStartDisabledByPolicy),
            _ => new AutoStartState(false),
        };
        return Task.FromResult(result);
    }

    public Task SetAutoStart(bool isEnabled)
    {
        var service = SMAppService.MainApp;
        var isDone = isEnabled
            ? service.Register(out var error)
            : service.Unregister(out error);
        if (!isDone)
            Log.LogWarning("Couldn't set auto-start to {IsEnabled}: {Error}", isEnabled, error?.LocalizedDescription);
        return Task.CompletedTask;
    }
}
