using ActualChat.Hosting;
using ActualChat.Permissions;
using MauiPermissions = Microsoft.Maui.ApplicationModel.Permissions;

namespace ActualChat.App.Maui.Services;

public class MauiContactsPermissionHandler : ContactsPermissionHandler
{
    public MauiContactsPermissionHandler(IServiceProvider services, bool mustStart = true)
        : base(services, false)
    {
        ExpirationPeriod = TimeSpan.FromMinutes(30); // No need to check this frequently
        if (mustStart)
            this.Start();
    }

    protected override async Task<bool?> Get(CancellationToken cancellationToken)
    {
        var status = await MauiPermissions.CheckStatusAsync<MauiPermissions.ContactsRead>().ConfigureAwait(false);
        Log.LogWarning("Get: CheckStatusAsync<MauiPermissions.ContactsRead>() response: {Status}", status);
        // Android returns Denied when permission is not set, also you can request permissions again
        return status switch {
            PermissionStatus.Granted => true,
            PermissionStatus.Limited => true,
            PermissionStatus.Unknown => null,
            PermissionStatus.Denied => HostInfo.ClientKind == ClientKind.Android ? null : false,
            _ => false,
        };
    }

    protected override async Task<bool> Request(CancellationToken cancellationToken)
    {
        var status = await MauiPermissions.RequestAsync<MauiPermissions.ContactsRead>().ConfigureAwait(false);
        return status is PermissionStatus.Granted or PermissionStatus.Limited;
    }

    protected override Task Troubleshoot(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
