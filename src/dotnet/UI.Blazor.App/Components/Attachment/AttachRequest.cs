using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public interface IAttachRequest
{
    Task CleanupForRemoving();
}

public record UploadSessionAttachRequest(UploadSessions UploadSessions, string UploadSessionId, IAttachRequest Inner) : IAttachRequest
{
    public async Task CleanupForRemoving()
    {
        await UploadSessions.CancelSessionIfNotCompleted(UploadSessionId).ConfigureAwait(false);
        await UploadSessions.DeleteSession(UploadSessionId).ConfigureAwait(false);
        if (Inner is AttachFileRequest) // Cleanup handled by the delete session.
            return;

        await Inner.CleanupForRemoving().ConfigureAwait(false);
    }
}

public record AttachFileRequest(IFileProvider FileProvider) : IAttachRequest
{
    public Task CleanupForRemoving()
        => FileProvider.ClearForRemoving();
}
