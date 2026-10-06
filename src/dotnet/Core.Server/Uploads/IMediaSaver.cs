namespace ActualChat.Uploads;

/// <summary>
/// Saves uploaded files as media records in storage.
/// </summary>
public interface IMediaSaver
{
    Task<MediaRef> Save(
        MediaId mediaId, UploadedFile file, Size2D? size, MediaKind kind,
        CancellationToken cancellationToken);
    Task<MediaRef> Save(
        MediaId mediaId, ProcessedFile file, bool isUpdate, MediaKind kind, UserId? userId,
        CancellationToken cancellationToken);
}

public static class MediaSaverExt
{
    public static Task<MediaRef> Save(
        this IMediaSaver mediaSaver,
        MediaId mediaId, ProcessedFile file, bool isUpdate, MediaKind kind,
        CancellationToken cancellationToken)
        => mediaSaver.Save(mediaId, file, isUpdate, kind, null, cancellationToken);
}
