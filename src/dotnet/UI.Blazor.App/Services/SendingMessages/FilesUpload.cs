namespace ActualChat.UI.Blazor.App.Services;

public class FilesUpload(ImmutableArray<Attachment> attachments, UploadFileRequestEntry[] uploadEntries)
{
    public int Count => uploadEntries.Length;
    public int ReferenceCount { get; private set; }

    public ImmutableArray<Attachment> GetAttachments()
        => attachments;

    public void ReviewUsages()
    {
        if (ReferenceCount > 0)
            return;

        // TODO: cleanup upload sessions
    }

    public void AddReference()
        => ReferenceCount++;

    public void Release()
    {
        ReferenceCount--;
        ReviewUsages();
    }

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
