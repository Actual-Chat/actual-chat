namespace ActualChat.App.Maui;

public static class PermissionsExt
{
    public static async Task EnsureGrantedAsync<TPermission>()
        where TPermission : Microsoft.Maui.ApplicationModel.Permissions.BasePermission, new()
    {
        var status = await Microsoft.Maui.ApplicationModel.Permissions
            .RequestAsync<TPermission>()
            .ConfigureAwait(true);

        if (status != PermissionStatus.Granted)
            throw new PermissionException($"{typeof(TPermission).Name} permission was not granted: {status}");
    }
}
