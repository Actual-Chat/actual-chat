namespace ActualChat.Streaming.UI.Blazor.Services;

public interface IRecordingPermissionRequester
{
    bool CanRequest { get; }
    Task<bool> TryRequest();
}
