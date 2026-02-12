namespace ActualChat.UI.Blazor.App.Services;

public class FilesUpload(ImmutableArray<Attachment> attachments, UploadFileRequestEntry[] uploadEntries, Func<Task> onRelease)
{
    public int Count => uploadEntries.Length;
    public ImmutableArray<Attachment> Attachments { get; } = attachments;
    public AttachFileRequestEntry[] CreateAttachFileRequests()
        => uploadEntries
            .Select(c => new AttachFileRequestEntry(
                c.UploadSessionId,
                c.FileName,
                c.FileType,
                c.FileLength,
                c.Width,
                c.Height
            ))
            .ToArray();
}

public record UploadFileRequestEntry(
    string UploadSessionId,
    string FileName,
    string FileType,
    long FileLength,
    int Width,
    int Height,
    AttachmentId AttachmentId
) : IHasId<string>
{
    string IHasId<string>.Id => UploadSessionId;
}
