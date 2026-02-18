namespace ActualChat.UI.Blazor.App.Services;

public record UploadSourceMetadata(
    string ContentType,
    long Length,
    string? FileName = null);

public interface IUploadSource
{
    UploadSourceMetadata Metadata { get; }
}
