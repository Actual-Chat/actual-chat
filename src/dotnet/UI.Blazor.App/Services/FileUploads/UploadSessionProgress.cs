namespace ActualChat.UI.Blazor.App.Services;

public sealed record UploadSessionProgress(UploadStage Stage, double StageProgress = 0, string Details = "")
{
    public static readonly UploadSessionProgress New = new(UploadStage.New);

    public bool IsReady => Stage == UploadStage.Completed;
    public bool IsFailed { get; init; }
    public string ErrorMessage { get; init; } = "";
    public long TotalBytes { get; init; }
    public double UploadedFraction
        // StageProgress is a 0..100 percentage within the current stage, not a 0..1 fraction
        => Stage switch {
            UploadStage.New or UploadStage.ClientProcessing => 0d,
            UploadStage.Uploading => Math.Clamp(StageProgress / 100d, 0d, 1d),
            _ => 1d, // Uploaded / ServerProcessing / Completed — bytes are already uploaded
        };
}
