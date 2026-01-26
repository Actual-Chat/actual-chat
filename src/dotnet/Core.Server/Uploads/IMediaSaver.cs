using SixLabors.ImageSharp;

namespace ActualChat.Uploads;

public interface IMediaSaver
{
    Task<MediaContent> Save(MediaId mediaId, UploadedFile file, Size? size, CancellationToken cancellationToken);
    Task<MediaContent> Save(MediaId mediaId, ProcessedFile file, bool isUpdate, CancellationToken cancellationToken);
}
