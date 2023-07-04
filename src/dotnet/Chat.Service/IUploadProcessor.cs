namespace ActualChat.Chat;

public interface IUploadProcessor
{
    bool Supports(FileInfo file);

    Task<ProcessedFileInfo> Process(FileInfo file, CancellationToken cancellationToken);
}


public sealed record ProcessedFileInfo(FileInfo File, Size? Size);

public sealed record FileInfo(string FileName, string ContentType, long Length, byte[] Content);
