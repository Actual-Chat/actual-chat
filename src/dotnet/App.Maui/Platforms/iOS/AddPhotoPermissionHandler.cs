﻿using ActualChat.Hosting;
using ActualChat.Permissions;
using ActualChat.UI.Blazor;
using MauiPermissions = Microsoft.Maui.ApplicationModel.Permissions;

namespace ActualChat.App.Maui;

public class AddPhotoPermissionHandler(UIHub hub, bool mustStart = true)
    : PermissionHandler(hub, mustStart)
{
    protected override async Task<bool?> Get(CancellationToken cancellationToken)
    {
        var status = await MauiPermissions.CheckStatusAsync<MauiPermissions.PhotosAddOnly>().ConfigureAwait(false);
        // Android returns Denied when permission is not set, also you can request permissions again
        return status switch {
            PermissionStatus.Granted => true,
            PermissionStatus.Limited => true,
            PermissionStatus.Unknown => null,
            PermissionStatus.Denied => HostInfo.AppKind == AppKind.Android ? null : false,
            _ => false,
        };
    }

    protected override async Task<bool> Request(CancellationToken cancellationToken)
    {
        var status = await MauiPermissions.RequestAsync<MauiPermissions.PhotosAddOnly>().ConfigureAwait(false);
        return status is PermissionStatus.Granted or PermissionStatus.Limited;
    }

    protected override Task Troubleshoot(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
