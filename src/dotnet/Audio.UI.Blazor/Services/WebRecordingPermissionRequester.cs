namespace ActualChat.Audio.UI.Blazor.Services;

public class WebRecordingPermissionRequester : IRecordingPermissionRequester
{
    public Task<bool> TryRequest()
        => Stl.Async.TaskExt.TrueTask;
}
