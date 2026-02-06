using SixLabors.ImageSharp;

namespace ActualChat.Uploads;

/// <summary>
/// Saves uploaded files as media records in storage.
/// </summary>
public interface IMediaSaver
{
    Task<Media.Media> Save(MediaId mediaId, UploadedFile file, Size? size, CancellationToken cancellationToken);
}
