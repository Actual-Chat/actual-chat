using ActualLab.IO;

namespace ActualChat.UI.Services;

public record UploadSourceMetadata(
    string ContentType,
    long Length,
    FilePath FileName = default);

public interface IUploadStreamSource;

public record UploadSource(UploadSourceMetadata Metadata, IUploadStreamSource StreamSource);
