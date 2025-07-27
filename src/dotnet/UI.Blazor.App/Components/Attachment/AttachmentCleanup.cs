using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public enum AttachmentCleanupKind { File, UploadSession, PersistedPostMessageRequest }

public record AttachmentCleanup(AttachmentCleanupKind Kind, Func<Task> Cleanup);

public class AttachmentCleanupCollection
{
    private readonly List<AttachmentCleanup> _items = new ();

    public IEnumerable<AttachmentCleanup> Items => _items;

    public void Add(AttachmentCleanup item)
        => _items.Add(item);

    public bool RemoveByKind(AttachmentCleanupKind kind)
        => _items.RemoveAll(x => x.Kind == kind) > 0;
}

public static class AttachmentCleanupFactory
{
    public static AttachmentCleanup ForFile(IFileProvider fileProvider)
        => new (AttachmentCleanupKind.File, fileProvider.ClearForRemoving);

    public static AttachmentCleanup ForUploadSession(UploadSessions uploadSessions, string uploadSessionId)
        => new (AttachmentCleanupKind.UploadSession,
            async () => {
                await uploadSessions.CancelSessionIfNotCompleted(uploadSessionId).ConfigureAwait(false);
                await uploadSessions.DeleteSession(uploadSessionId).ConfigureAwait(false);
            });
}
