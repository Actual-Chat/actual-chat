namespace ActualChat.UI.Blazor.App.Services;

public sealed class AttachmentUploads : IDisposable
{
    private readonly AttachmentRegistry _attachmentRegistry;
    private readonly TaskCompletionSource _whenUploaded = TaskCompletionSourceExt.New();
    private CancellationTokenSource? _cts;

    public IAttachmentList Attachments { get; }
    public Task WhenUploaded => _whenUploaded.Task;

    public AttachmentUploads(IAttachmentList attachments, AttachmentRegistry attachmentRegistry)
    {
        if (attachments.Count is 0)
            throw new ArgumentException("Attachments must not be empty.", nameof(attachments));

        _attachmentRegistry = attachmentRegistry;
        Attachments = attachments;
        attachments.Changed += OnAttachmentsChanged;
        ReviewState();
    }

    public void Dispose()
    {
        Attachments.Changed -= OnAttachmentsChanged;
        _cts?.CancelAndDisposeSilently();
        _cts = null;
    }

    private void ReviewState()
    {
        _cts?.CancelAndDisposeSilently();
        _cts = new CancellationTokenSource();
        _ = WatchUploadsAsync(_cts.Token);
    }

    private void OnAttachmentsChanged(object? sender, EventArgs e)
        => ReviewState();

    private async Task WatchUploadsAsync(CancellationToken cancellationToken)
    {
        try {
            var tasks = Attachments.Items.Select(x => WhenAttachmentReady(x.Id, cancellationToken));
            await Task.WhenAll(tasks).ConfigureAwait(false);
            _whenUploaded.TrySetResult();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            // Expected on Dispose or ReviewState
        }
        catch {
            // Intended
        }
    }

    private async Task WhenAttachmentReady(AttachmentId attachmentId, CancellationToken cancellationToken)
    {
        var computed = await Computed
            .Capture(() => _attachmentRegistry.IsReady(attachmentId, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        await computed.When(x => x, cancellationToken).ConfigureAwait(false);
    }
}
