namespace ActualChat.UI.Blazor.App.Components;

public sealed class AttachmentList2 : IAttachmentList
{
    private readonly List<(Attachment, AttachmentUploadOperation)> _attachments;
    private bool _isDisposed;
    public int Count => _attachments.Count;
    public IEnumerable<Attachment> Items => _attachments.Select(x => x.Item1);
    public event EventHandler? Changed;

    public AttachmentList2(IReadOnlyCollection<(Attachment, AttachmentUploadOperation)> attachments)
    {
        _attachments = attachments.ToList();
        foreach (var (_, uploadOperation) in _attachments)
            uploadOperation.Updated += OnUpdated;
    }

    public async Task Remove(Attachment attachment)
    {
        var found = _attachments.FirstOrDefault(c => c.Item1 == attachment);
        if (found.Item1 is null)
            throw StandardError.Internal("Attachment not found.");

        _attachments.Remove(found);
        var uploadOperation = found.Item2;
        await uploadOperation.DisposeSilentlyAsync();
        uploadOperation.Updated -= OnUpdated;
        OnChanged();
    }

    private void OnUpdated(object? sender, EventArgs e)
        => OnChanged();

    private void OnChanged()
        => Changed?.Invoke(this, EventArgs.Empty);

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        var tasks = new List<Task>();
        foreach (var (_, uploadOperation) in _attachments) {
            tasks.Add(uploadOperation.DisposeSilentlyAsync().AsTask());
            uploadOperation.Updated -= OnUpdated;
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }
}
