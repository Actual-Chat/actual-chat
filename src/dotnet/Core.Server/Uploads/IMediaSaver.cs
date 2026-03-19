using SixLabors.ImageSharp;

namespace ActualChat.Uploads;

/// <summary>
/// Saves uploaded files as media records in storage.
/// </summary>
public interface IMediaSaver
{
    Task<MediaRef> Save(MediaId mediaId, UploadedFile file, Size? size, MediaKind kind, CancellationToken cancellationToken);
    Task<MediaRef> Save(MediaId mediaId, ProcessedFile file, bool isUpdate, MediaKind kind, CancellationToken cancellationToken);
}
