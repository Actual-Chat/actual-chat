namespace ActualChat.UI.Blazor.App.Components;

public sealed class AttachmentUploadOperation : IAsyncDisposable
{
    private static readonly ILogger Log = StaticLog.For<AttachmentUploadOperation>();

    private bool _isDisposed;
    private Attachment _attachment;
    private readonly CancellationTokenSource _cancellationTokenSource;

    public event EventHandler? Updated;
    public Attachment Attachment => _attachment;

    public AttachmentUploadOperation(
        Attachment attachment,
        Task<MediaContent> fileUpload,
        CancellationTokenSource cancellationTokenSource)
    {
        _cancellationTokenSource = cancellationTokenSource;
        _attachment = attachment;
        TrackProgress(fileUpload);
    }

    private void TrackProgress(Task<MediaContent> fileUpload)
        => _ = BackgroundTask.Run(async () => {
            try {
                var uploadResult = await fileUpload.ConfigureAwait(false);
                _attachment = _attachment with {
                    Progress = 100,
                    MediaId = uploadResult.MediaId,
                    ThumbnailMediaId = uploadResult.ThumbnailMediaId,
                };
            }
            catch (OperationCanceledException) {
                // Ignore
            }
            catch (Exception e) {
                Log.LogError(e, "Failed to upload file for sharing");
                _attachment = _attachment with {
                    Failed = true,
                };
            }
            RaiseUpdated();
        });

    private void RaiseUpdated()
        => Updated?.Invoke(this, EventArgs.Empty);

    public Task Cancel()
    {
        _cancellationTokenSource.Cancel();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        await Cancel().ConfigureAwait(false);
        _cancellationTokenSource.DisposeSilently();
    }
}
