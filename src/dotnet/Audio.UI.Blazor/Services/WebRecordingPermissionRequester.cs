namespace ActualChat.Audio.UI.Blazor.Services;

public class WebRecordingPermissionRequester : IRecordingPermissionRequester
{
    public bool CanRequest => false;

    public Task<bool> TryRequest()
        => Stl.Async.TaskExt.FalseTask;
}
