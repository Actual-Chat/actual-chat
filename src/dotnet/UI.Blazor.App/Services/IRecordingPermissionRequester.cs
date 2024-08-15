namespace ActualChat.UI.Blazor.App.Services;

public interface IRecordingPermissionRequester
{
    bool CanRequest { get; }
    Task<bool> TryRequest();
}
