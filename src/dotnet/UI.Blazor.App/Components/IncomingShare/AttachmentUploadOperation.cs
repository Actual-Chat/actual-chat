using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public sealed class AttachmentUploadOperation : IAsyncDisposable
{
    private static readonly ILogger Log = StaticLog.For<AttachmentUploadOperation>();

    private bool _isDisposed;
    private Attachment _attachment;
    private readonly FileUploadOperation<MediaContent> _fileUploadOperation;

    public event EventHandler? Updated;
    public Attachment Attachment => _attachment;

    public AttachmentUploadOperation(
        Attachment attachment,
        FileUploadOperation<MediaContent> fileUploadOperation)
    {
        _attachment = attachment;
        _fileUploadOperation = fileUploadOperation;
        StartTrackProgress();
    }

    public Task Cancel()
    {
        _fileUploadOperation.Cancel();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        await Cancel().ConfigureAwait(false);
    }

    private void StartTrackProgress()
    {
        _fileUploadOperation.Progress?.ProgressChanged += (_, value) => {
            _attachment = _attachment with {
                // Max progress value is limited to 99%.
                // 100% is applied when the upload result is received.
                Progress = Math.Min(99, (int)value),
            };
            RaiseUpdated();
        };
        _ = BackgroundTask.Run(async () => {
            try {
                var uploadResult = await _fileUploadOperation.Task.ConfigureAwait(false);
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
    }

    private void RaiseUpdated()
        => Updated?.Invoke(this, EventArgs.Empty);
}
