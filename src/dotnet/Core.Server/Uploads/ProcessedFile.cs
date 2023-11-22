namespace ActualChat.Uploads;

public sealed record ProcessedFile(UploadedFile File, Size? Size, UploadedFile? Thumbnail = null) : IDisposable
{
    public void Dispose()
    {
        (File as UploadedTempFile)?.Delete();
        (Thumbnail as UploadedTempFile)?.Delete();
    }
}
