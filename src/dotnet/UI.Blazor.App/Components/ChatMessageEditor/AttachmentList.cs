using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public class AttachmentList(Action<IFileUploadOperation> enqueueFileUploadOperation) : IAttachmentList, IAttachmentListBackend
{
    public static Exception FileTooBigError()
        => StandardError.Constraint($"File is too big. Max file size: {Constants.Attachments.FileSizeLimit / 1024 / 1024}Mb.");

    private readonly Lock _lock = new();
    private ImmutableList<AttachmentInfo> _attachments = ImmutableList<AttachmentInfo>.Empty;
    private IJSObjectReference? JSRef { get; set; }
    private DotNetObjectReference<IAttachmentListBackend>? BlazorRef { get; set; }

    public int Count => _attachments.Count;
    public IEnumerable<Attachment> Items => _attachments.Select(x => x.Attachment);
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
            var attachmentInfo = _attachments.Find(c => c.Attachment == attachment);
            if (attachmentInfo is null)
                throw StandardError.Internal("Attachment not found.");
            _attachments = _attachments.Remove(attachmentInfo);
            attachmentInfo.UploadOperation.Dispose();
        }
        await InvokeRemove(attachment);
        OnChanged();
    }

    public async Task Clear()
    {
        lock (_lock) {
            var clone = _attachments;
            _attachments = _attachments.Clear();
            foreach (var info in clone)
                info.UploadOperation.Dispose();
        }

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

            var attachment = new Attachment(id, url, fileName ?? "", fileType ?? "", length);
            var progress = new Progress<double>();
            var tcs = TaskCompletionSourceExt.New<MediaContent>();
            var uploadOperation = new FileUploadOperation<MediaContent>(async ct => {
                ct.Register(() => {
                    tcs.TrySetCanceled();
                    _ = InvokeCancelUpload(attachment.Id);
                });
                await InvokeStartUpload(attachment.Id);
                return await tcs.Task;
            }) {
                Progress = progress,
            };
            _attachments = _attachments.Add(new AttachmentInfo(attachment, uploadOperation, progress, tcs));
            return null;
        }
    }

    [JSInvokable]
    public void OnUploaderPrepared(int id) {
        var info = FindAttachmentById(id);
        if (info is null)
            return;

        var uploadOperation = info.UploadOperation;
        //uploadOperation.Start();
        enqueueFileUploadOperation(uploadOperation);
    }

    [JSInvokable]
    public void OnUploadProgress(int id, int progress) {
        var info = FindAttachmentById(id);
        if (info is not null)
            ((IProgress<double>)info.Progress).Report(progress);
        UpdateAttachment(id, x => x with { Progress = progress });
        OnChanged();
    }

    [JSInvokable]
    public void OnUploadSucceed(int id, MediaId mediaId, MediaId thumbnailMediaId) {
        var info = FindAttachmentById(id);
        if (info is not null)
            info.TaskSource.TrySetResult(new MediaContent(mediaId, "", thumbnailMediaId, ""));
        UpdateAttachment(id, x => x with { MediaId = mediaId, ThumbnailMediaId = thumbnailMediaId });
        OnChanged();
    }

    [JSInvokable]
    public void OnUploadFailed(int id) {
        var info = FindAttachmentById(id);
        if (info is not null)
            info.TaskSource.TrySetException(StandardError.Internal("Upload failed."));
        UpdateAttachment(id, x => x with { Failed = true });
        OnChanged();
    }

    private void OnChanged()
        => Changed?.Invoke(this, EventArgs.Empty);

    private ValueTask InvokeClear()
        => JSRef?.InvokeVoidAsync("clear") ?? ValueTask.CompletedTask;

    private ValueTask InvokeRemove(Attachment attachment)
        => JSRef?.InvokeVoidAsync("remove", attachment.Id) ?? ValueTask.CompletedTask;

    private ValueTask InvokeStartUpload(int attachmentId)
        => JSRef?.InvokeVoidAsync("startUpload", attachmentId) ?? ValueTask.CompletedTask;

    private ValueTask InvokeCancelUpload(int attachmentId)
        => JSRef?.InvokeVoidAsync("cancelUpload", attachmentId) ?? ValueTask.CompletedTask;

    private AttachmentInfo? FindAttachmentById(int id)
        => _attachments.Find(c => c.Attachment.Id == id);

    private void UpdateAttachment(int id, Func<Attachment, Attachment> updater) {
        lock (_lock) {
            var i = _attachments.FindIndex(x => x.Attachment.Id == id);
            if (i < 0)
                return;

            var info = _attachments[i];
            var newInfo = info with { Attachment = updater(info.Attachment) };
            _attachments = _attachments.SetItem(i, newInfo);
        }
    }

    private sealed record AttachmentInfo(
        Attachment Attachment,
        FileUploadOperation<MediaContent> UploadOperation,
        Progress<double> Progress,
        TaskCompletionSource<MediaContent> TaskSource);
}
