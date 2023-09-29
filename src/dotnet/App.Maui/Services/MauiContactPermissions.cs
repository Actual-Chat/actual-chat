using ActualChat.Contacts.UI.Blazor.Services;
using ActualChat.UI.Blazor;
using AppInfo = Microsoft.Maui.ApplicationModel.AppInfo;
using Dispatcher = Microsoft.AspNetCore.Components.Dispatcher;
using MauiPermissions = Microsoft.Maui.ApplicationModel.Permissions;

namespace ActualChat.App.Maui.Services;

public class MauiContactPermissions(IServiceProvider services) : IContactPermissions
{
    protected Dispatcher Dispatcher { get; } = services.GetRequiredService<Dispatcher>();

    public virtual async Task<PermissionState> GetState()
    {
        var status = await Dispatcher.InvokeAsync(MauiPermissions.CheckStatusAsync<MauiPermissions.ContactsRead>)
            .ConfigureAwait(false);
        return ToPermissionState(status);
    }

    public virtual async Task<PermissionState> Request()
    {
        var status = await Dispatcher.InvokeAsync(MauiPermissions.RequestAsync<MauiPermissions.ContactsRead>)
            .ConfigureAwait(false);
        using (Computed.Invalidate())
            _ = GetState();
        return ToPermissionState(status);
    }

    public Task OpenSettings()
        => Dispatcher.InvokeAsync(AppInfo.Current.ShowSettingsUI);

    private static PermissionState ToPermissionState(PermissionStatus status)
        => status switch {
            PermissionStatus.Denied => PermissionState.Denied,
            PermissionStatus.Disabled => PermissionState.Denied,
            PermissionStatus.Restricted => PermissionState.Denied,
            PermissionStatus.Limited => PermissionState.Denied,
            PermissionStatus.Granted => PermissionState.Granted,
            _ => PermissionState.Prompt,
        };
}
