using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;
using Contacts;

namespace ActualChat.App.Maui;

// TODO(maui-labs): see MacOSMediaCapture
/// <summary>
/// AppKit twin of <see cref="MauiContactsPermissionHandler"/> on CNContactStore.
/// </summary>
public sealed class MacOSContactsPermissionHandler : ContactsPermissionHandler
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
        bool? isGranted = CNContactStore.GetAuthorizationStatus(CNEntityType.Contacts) switch {
            CNAuthorizationStatus.NotDetermined => null,
            CNAuthorizationStatus.Authorized => true,
            CNAuthorizationStatus.Limited => true,
            _ => false,
        };
        Log.LogInformation("Get: {IsGranted}", isGranted);
        return Task.FromResult(isGranted);
    }

    protected override async Task<bool> Request(CancellationToken cancellationToken)
    {
        using var store = new CNContactStore();
        var (isGranted, _) = await store.RequestAccessAsync(CNEntityType.Contacts).ConfigureAwait(false);
        return isGranted;
    }

    protected override Task Troubleshoot(CancellationToken cancellationToken)
        => OpenSystemSettings();
}
