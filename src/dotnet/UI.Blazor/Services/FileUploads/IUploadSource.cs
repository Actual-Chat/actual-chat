namespace ActualChat.UI.Blazor.Services;

public record UploadSourceMetadata(
    string ContentType,
    long Length,
    string? FileName = null);

public interface IUploadStreamSource;

public record UploadSource(UploadSourceMetadata Metadata, IUploadStreamSource StreamSource);
