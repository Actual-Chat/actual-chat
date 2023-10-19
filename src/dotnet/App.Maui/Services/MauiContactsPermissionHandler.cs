using ActualChat.Permissions;
using MauiPermissions = Microsoft.Maui.ApplicationModel.Permissions;

namespace ActualChat.App.Maui.Services;

public class MauiContactsPermissionHandler : ContactsPermissionHandler
{
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
        var status = await MauiPermissions.RequestAsync<MauiPermissions.ContactsRead>().ConfigureAwait(false);
        return status is PermissionStatus.Granted or PermissionStatus.Limited;
    }

    protected override Task Troubleshoot(CancellationToken cancellationToken)
        => OpenSystemSettings();
}
