namespace ActualChat.UI.Blazor.App.Services;

public static class CallStageExt
{
    public static bool IsInProgress(this CallStage stage)
        => stage is CallStage.Requesting or CallStage.Calling or CallStage.Ringing or CallStage.Connecting;
}
