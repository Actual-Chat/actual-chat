using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public enum AttachmentCleanupKind
{
    PendingWork,
    File,
    UploadSession,
    PersistedPostMessageRequest,
    SourceFile,
    PreviewFile,
}

public sealed record AttachmentCleanup(AttachmentCleanupKind Kind, Func<Task> Cleanup);

public sealed class AttachmentCleanupCollection
{
    private readonly Lock _lock = new();
    private readonly List<AttachmentCleanup> _items = new ();

    public IEnumerable<AttachmentCleanup> Items {
        // A snapshot, taken under the lock: the cleanups run on a background task while the
        // dispatcher may still add the upload session's one to the very same collection
        get {
            lock (_lock)
                return _items.ToArray();
        }
    }

    public void Add(AttachmentCleanup item)
    {
        lock (_lock)
            _items.Add(item);
    }

    public bool RemoveByKind(AttachmentCleanupKind kind)
    {
        lock (_lock)
            return _items.RemoveAll(x => x.Kind == kind) > 0;
    }
}

public static class AttachmentCleanupFactory
{
    public static AttachmentCleanup ForPendingWork(Action cancel)
        // Registered first, so the encode/upload in flight is cancelled before the cleanups
        // below tear down the files it reads
        => new (AttachmentCleanupKind.PendingWork,
            () => {
                cancel.Invoke();
                return Task.CompletedTask;
            });

    public static AttachmentCleanup ForFile(IFileProvider fileProvider)
        => new (AttachmentCleanupKind.File, fileProvider.ClearForRemoving);

    public static AttachmentCleanup ForSourceFile(IFileProvider fileProvider)
        // Unlike ForFile, InitUploadSession doesn't replace it: the source outlives the processed file's session
        => new (AttachmentCleanupKind.SourceFile, fileProvider.ClearForRemoving);

    public static AttachmentCleanup ForPreviewFile(IFileProvider fileProvider)
        // Its own kind for the same reason as SourceFile: InitUploadSession's RemoveByKind(File) must not
        // drop it, since it's unrelated to whatever file the committed pipeline ends up uploading
        => new (AttachmentCleanupKind.PreviewFile, fileProvider.ClearForRemoving);

    public static AttachmentCleanup ForUploadSession(UploadSessions uploadSessions, string uploadSessionId)
        => new (AttachmentCleanupKind.UploadSession,
            () => {
                uploadSessions.ReleaseReference(uploadSessionId);
                return Task.CompletedTask;
            });

    public static AttachmentCleanup ForStaleUploadSession(UploadSessions uploadSessions, string uploadSessionId)
        => new (AttachmentCleanupKind.UploadSession,
            () => uploadSessions.DeleteStaleSession(uploadSessionId));
}
