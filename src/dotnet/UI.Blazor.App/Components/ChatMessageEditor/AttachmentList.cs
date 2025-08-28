namespace ActualChat.UI.Blazor.App.Components;

public class AttachmentList : IAttachmentList, IAttachmentListBackend
{
    private readonly Lock _lock = new();
    private ImmutableList<Attachment> _attachments = ImmutableList<Attachment>.Empty;
    private IJSObjectReference? JSRef { get; set; }
    private DotNetObjectReference<IAttachmentListBackend>? BlazorRef { get; set; }

    public int Count => _attachments.Count;
    public IEnumerable<Attachment> Items => _attachments;
    public event EventHandler? Changed;
    public event EventHandler<Exception>? FailedToAdd;

    public async Task AttachTo(IJSObjectReference jsRef)
    {
        if (BlazorRef != null)
            throw StandardError.Internal("Already attached.");

        BlazorRef = DotNetObjectReference.Create<IAttachmentListBackend>(this);
        JSRef = await jsRef.InvokeAsync<IJSObjectReference>("attachList", BlazorRef);
    }

    public async ValueTask DisposeAsync() {
        await JSRef.DisposeSilentlyAsync("dispose");
        JSRef = null;
        BlazorRef.DisposeSilently();
        BlazorRef = null;
    }

    public async Task Remove(Attachment attachment) {
        lock (_lock) {
            if (!_attachments.Contains(attachment))
                throw StandardError.Internal("Attachment not found.");
            _attachments = _attachments.Remove(attachment);
        }
        await InvokeRemove(attachment);
        OnChanged();
    }

    public async Task Clear() {
        lock (_lock)
            _attachments = ImmutableList<Attachment>.Empty;

        await InvokeClear();
        OnChanged();
    }

    [JSInvokable]
    public bool OnAttachmentAdded(int id, string url, string? fileName, string? fileType, int length) {
        var error = TryAdd();
        if (error != null) {
            FailedToAdd?.Invoke(this, error);
            return false;
        }

        OnChanged();
        return true;

        Exception? TryAdd() {
            if (length > Constants.Attachments.FileSizeLimit)
                return FileTooBigError();

            if (_attachments.Count >= Constants.Attachments.FileCountLimit)
                return StandardError.Constraint("Too many files. Max allowed number is 10.");

            _attachments = _attachments.Add(new(id, url, fileName ?? "", fileType ?? "", length));
            return null;
        }
    }

    public static Exception FileTooBigError()
        => StandardError.Constraint($"File is too big. Max file size: {Constants.Attachments.FileSizeLimit / 1024 / 1024}Mb.");

    [JSInvokable]
    public void OnUploadProgress(int id, int progress) {
        UpdateAttachment(id, x => x with { Progress = progress });
        OnChanged();
    }

    [JSInvokable]
    public void OnUploadSucceed(int id, MediaId mediaId, MediaId thumbnailMediaId) {
        UpdateAttachment(id, x => x with { MediaId = mediaId, ThumbnailMediaId = thumbnailMediaId });
        OnChanged();
    }

    [JSInvokable]
    public void OnUploadFailed(int id) {
        UpdateAttachment(id, x => x with { Failed = true });
        OnChanged();
    }

    private void OnChanged()
        => Changed?.Invoke(this, EventArgs.Empty);

    private ValueTask InvokeClear()
        => JSRef?.InvokeVoidAsync("clear") ?? ValueTask.CompletedTask;

    private ValueTask InvokeRemove(Attachment attachment)
        => JSRef?.InvokeVoidAsync("remove", attachment.Id) ?? ValueTask.CompletedTask;

    private void UpdateAttachment(int id, Func<Attachment, Attachment> updater) {
        lock (_lock) {
            var i = _attachments.FindIndex(x => x.Id == id);
            if (i < 0)
                return;

            _attachments = _attachments.SetItem(i, updater(_attachments[i]));
        }
    }
}
