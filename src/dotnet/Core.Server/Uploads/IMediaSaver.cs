using SixLabors.ImageSharp;

namespace ActualChat.Uploads;

public interface IMediaSaver
{
    Task<Media.Media> Save(MediaId mediaId, UploadedFile file, Size? size, CancellationToken cancellationToken);
}
