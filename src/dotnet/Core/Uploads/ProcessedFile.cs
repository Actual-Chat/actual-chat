namespace ActualChat.Uploads;

public sealed record ProcessedFile(UploadedFile File, Size? Size) : IDisposable
{
    public void Dispose()
        => (File as UploadedTempFile)?.Delete();
}
