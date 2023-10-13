using ActualChat.Permissions;
using AppInfo = Microsoft.Maui.ApplicationModel.AppInfo;
using Dispatcher = Microsoft.AspNetCore.Components.Dispatcher;
using MauiPermissions = Microsoft.Maui.ApplicationModel.Permissions;

namespace ActualChat.App.Maui.Services;

public class MauiContactsPermissionHandler : ContactsPermissionHandler
{
    private Dispatcher? _dispatcher;
    private Dispatcher Dispatcher => _dispatcher ??= Services.GetRequiredService<Dispatcher>();

    public MauiContactsPermissionHandler(IServiceProvider services, bool mustStart = true)
        : base(services, mustStart)
        => ExpirationPeriod = null; // We don't need expiration period - GrantContactPermissionBanner checks it

    protected override async Task<bool?> Get(CancellationToken cancellationToken)
    {
        var status = await Dispatcher
            .InvokeAsync(MauiPermissions.CheckStatusAsync<MauiPermissions.ContactsRead>)
            .ConfigureAwait(false);
        return status switch {
            PermissionStatus.Denied => false,
            PermissionStatus.Disabled => false,
            PermissionStatus.Restricted => false,
            PermissionStatus.Unknown => null,
            PermissionStatus.Granted => true,
            PermissionStatus.Limited => true,
            _ => throw StandardError.NotSupported<PermissionStatus>(status.ToString()),
        };
    }

    protected override async Task<bool> Request(CancellationToken cancellationToken)
    {
        var status = await Dispatcher.InvokeAsync(MauiPermissions.RequestAsync<MauiPermissions.ContactsRead>)
            .ConfigureAwait(false);
        return status is PermissionStatus.Granted or PermissionStatus.Limited;
    }

    protected override Task<bool> Troubleshoot(CancellationToken cancellationToken)
        => Stl.Async.TaskExt.FalseTask;

    public override Task OpenSettings()
        => Dispatcher.InvokeAsync(AppInfo.Current.ShowSettingsUI);
}
