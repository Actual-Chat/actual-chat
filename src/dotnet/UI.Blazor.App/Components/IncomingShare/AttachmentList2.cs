namespace ActualChat.UI.Blazor.App.Components;

public sealed class AttachmentList2 : IAttachmentList
{
    private readonly List<AttachmentUploadOperation> _attachments;
    private readonly SynchronizationContext _syncContext;
    private bool _isDisposed;
    public int Count => _attachments.Count;
    public IEnumerable<Attachment> Items => _attachments.Select(x => x.Attachment);
    public event EventHandler? Changed;

    public AttachmentList2(IReadOnlyCollection<AttachmentUploadOperation> attachments)
    {
        _syncContext = SynchronizationContext.Current ?? new SynchronizationContext();
        _attachments = attachments.ToList();
        foreach (var uploadOperation in _attachments)
            uploadOperation.Updated += OnUpdated;
    }

    public async Task Remove(Attachment attachment)
    {
        var uploadOperation = _attachments.FirstOrDefault(c => c.Attachment.Id == attachment.Id);
        if (uploadOperation is null)
            throw StandardError.Internal("Attachment not found.");

        _attachments.Remove(uploadOperation);
        await uploadOperation.DisposeSilentlyAsync();
        uploadOperation.Updated -= OnUpdated;
        OnChanged();
    }

    private void OnUpdated(object? sender, EventArgs e)
        => OnChanged();

    private void OnChanged()
        => _syncContext.Post(_ => {
            Changed?.Invoke(this, EventArgs.Empty);
        }, null);

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        var tasks = new List<Task>();
        foreach (var uploadOperation in _attachments) {
            tasks.Add(uploadOperation.DisposeSilentlyAsync().AsTask());
            uploadOperation.Updated -= OnUpdated;
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }
}
