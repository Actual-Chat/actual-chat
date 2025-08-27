namespace ActualChat.UI.Blazor.App.Components;

public class AttachmentList : IAttachmentListBackend, IAsyncDisposable
{
    private readonly Lock _lock = new();
    private ImmutableList<Attachment> _attachments = ImmutableList<Attachment>.Empty;
    private IJSObjectReference? JSRef { get; set; }
    private DotNetObjectReference<IAttachmentListBackend>? BlazorRef { get; set; }

    public int Count => _attachments.Count;
    public IEnumerable<Attachment> Items => _attachments;
    public EventHandler? Changed;
    public EventHandler<Exception>? FailedToAdd { get; set; }

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

    public Task Remove(Attachment attachment) {
        lock (_lock)
            _attachments = _attachments.Remove(attachment);

        return JSRef?.InvokeVoidAsync("remove", attachment.Id).AsTask() ?? Task.CompletedTask;
    }

    public async Task Clear() {
        lock (_lock)
            _attachments = ImmutableList<Attachment>.Empty;

        await InvokeClear();
        StateHasChanged();
    }

    [JSInvokable]
    public bool OnAttachmentAdded(int id, string url, string? fileName, string? fileType, int length) {
        var error = TryAdd();
        if (error != null) {
            FailedToAdd?.Invoke(this, error);
            return false;
        }

        StateHasChanged();
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
        StateHasChanged();
    }

    private void StateHasChanged()
        => Changed?.Invoke(this, EventArgs.Empty);

    [JSInvokable]
    public void OnUploadSucceed(int id, MediaId mediaId, MediaId thumbnailMediaId) {
        UpdateAttachment(id, x => x with { MediaId = mediaId, ThumbnailMediaId = thumbnailMediaId });
        StateHasChanged();
    }

    [JSInvokable]
    public void OnUploadFailed(int id) {
        UpdateAttachment(id, x => x with { Failed = true });
        StateHasChanged();
    }

    private ValueTask InvokeClear()
        => JSRef?.InvokeVoidAsync("clear") ?? ValueTask.CompletedTask;

    private void UpdateAttachment(int id, Func<Attachment, Attachment> updater) {
        lock (_lock) {
            var i = _attachments.FindIndex(x => x.Id == id);
            if (i < 0)
                return;

            _attachments = _attachments.SetItem(i, updater(_attachments[i]));
        }
    }
}
