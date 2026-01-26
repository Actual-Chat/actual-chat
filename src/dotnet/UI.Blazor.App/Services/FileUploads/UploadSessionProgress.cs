namespace ActualChat.UI.Blazor.App.Services;

public sealed record UploadSessionProgress(UploadStage Stage, double Progress = 0, string Details = "")
{
    public static readonly UploadSessionProgress New = new(UploadStage.New);

    public bool IsReady => Stage == UploadStage.Completed;
    public bool IsFailed { get; init; }
    public string ErrorMessage { get; init; } = "";
}
