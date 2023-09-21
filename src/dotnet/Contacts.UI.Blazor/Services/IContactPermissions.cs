namespace ActualChat.Contacts.UI.Blazor.Services;

public interface IContactPermissions
{
    public Task<PermissionState> GetState()
        => Task.FromResult(PermissionState.Granted);

    public Task<PermissionState> Request()
        => Task.FromResult(PermissionState.Granted);

    public Task OpenSettings()
        => Task.CompletedTask;
}
