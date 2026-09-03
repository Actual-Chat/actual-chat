using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui;

/// <summary>
/// AppKit twin of <see cref="MauiContactsPermissionHandler"/> on <see cref="MacOSPermissions"/>.
/// </summary>
public class MacOSContactsPermissionHandler : ContactsPermissionHandler
{
    public MacOSContactsPermissionHandler(UIHub hub, bool mustStart = true)
        : base(hub, false)
    {
        ExpirationPeriod = TimeSpan.FromMinutes(30);
        if (mustStart)
            this.Start();
    }

    protected override Task<bool?> Get(CancellationToken cancellationToken)
    {
        var isGranted = MacOSPermissions.IsContactsGranted();
        Log.LogInformation("Get: {IsGranted}", isGranted);
        return Task.FromResult(isGranted);
    }

    protected override Task<bool> Request(CancellationToken cancellationToken)
        => MacOSPermissions.RequestContacts();

    protected override Task Troubleshoot(CancellationToken cancellationToken)
        => OpenSystemSettings();
}
