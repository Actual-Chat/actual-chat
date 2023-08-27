namespace ActualChat.App.Maui;

public static class PermissionsEx
{
    public static async Task EnsureGrantedAsync<TPermission>()
        where TPermission : Microsoft.Maui.ApplicationModel.Permissions.BasePermission, new()
    {
        var status = await Microsoft.Maui.ApplicationModel.Permissions.RequestAsync<TPermission>();

        if (status != PermissionStatus.Granted)
            throw new PermissionException($"{typeof(TPermission).Name} permission was not granted: {status}");
    }
}
