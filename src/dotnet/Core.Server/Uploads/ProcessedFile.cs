using SixLabors.ImageSharp;

namespace ActualChat.Uploads;

/// <summary>
/// Result of processing an uploaded file, including optional thumbnail.
/// </summary>
public sealed record ProcessedFile(UploadedFile File, Size? Size, UploadedFile? Thumbnail = null) : IDisposable
{
    public void Dispose()
    {
        (File as UploadedTempFile)?.Delete();
        (Thumbnail as UploadedTempFile)?.Delete();
    }
}
