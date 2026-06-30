namespace ActualChat.Uploads;

/// <summary>
/// Result of processing an uploaded file, including optional thumbnail.
/// </summary>
public sealed record ProcessedFile(UploadedFile File, Size2D? Size, UploadedFile? Thumbnail = null) : IDisposable
{
    public Action? OnDispose { get; init; }
    public TimeSpan? Duration { get; init; }

    public void Dispose()
    {
        (File as UploadedTempFile)?.Delete();
        (Thumbnail as UploadedTempFile)?.Delete();
        try { OnDispose?.Invoke(); } catch { /* best-effort */ }
    }
}
