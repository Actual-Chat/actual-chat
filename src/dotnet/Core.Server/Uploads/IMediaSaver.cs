using SixLabors.ImageSharp;

namespace ActualChat.Uploads;

/// <summary>
/// Saves uploaded files as media records in storage.
/// </summary>
public interface IMediaSaver
{
    Task<MediaContent> Save(MediaId mediaId, UploadedFile file, Size? size, CancellationToken cancellationToken);
    Task<MediaContent> Save(MediaId mediaId, ProcessedFile file, bool isUpdate, CancellationToken cancellationToken);
}
