namespace ActualChat.Audio.UI.Blazor.Services;

public interface IRecordingPermissionRequester
{
    bool CanRequest { get; }
    Task<bool> TryRequest();
}
