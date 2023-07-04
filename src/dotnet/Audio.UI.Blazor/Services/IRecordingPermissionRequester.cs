namespace ActualChat.Audio.UI.Blazor.Services;

public interface IRecordingPermissionRequester
{
    Task<bool> TryRequest();
}
