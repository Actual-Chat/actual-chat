namespace ActualChat.UI.Blazor.App.Services;

public class WebRecordingPermissionRequester : IRecordingPermissionRequester
{
    public bool CanRequest => false;

    public Task<bool> TryRequest()
        => ActualLab.Async.TaskExt.FalseTask;
}
