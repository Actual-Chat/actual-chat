using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public class AttachmentList(UploadSessionManager uploadSessionManager, ILogger<AttachmentList> log) : IAttachmentList, IAttachmentListBackend
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
        AttachmentInfo? attachmentInfo;
        lock (_lock) {
            attachmentInfo = _attachments.Find(c => c.Attachment == attachment);
            if (attachmentInfo is null)
                throw StandardError.Internal("Attachment not found.");
            _attachments = _attachments.Remove(attachmentInfo);
        }
        if (attachmentInfo.UploadSession is not null)
            await uploadSessionManager.CancelSession(attachmentInfo.UploadSession.SessionId);
        await InvokeRemove(attachment);
        OnChanged();
    }

    public async Task Clear()
    {
        ImmutableList<AttachmentInfo> clone;
        lock (_lock) {
            clone = _attachments;
            _attachments = _attachments.Clear();
        }
        foreach (var attachmentInfo in clone) {
            if (attachmentInfo.UploadSession is not null)
                await uploadSessionManager.CancelSession(attachmentInfo.UploadSession.SessionId).ConfigureAwait(false);
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
            _attachments = _attachments.Add(new AttachmentInfo(attachment));
            return null;
        }
    }

    [JSInvokable]
    public async Task OnCreateUploaderRequested(int id, string sChatId) {
        var info = FindAttachmentById(id);
        if (info is null)
            return;

        WebFileProviderInternal? webFileProviderInternal;
        var chatId = ChatId.Parse(sChatId);
        var fileUploaderBackend = new FileUploaderBackend();
        try {
            var webProviderInternalRef = await JSRef!
                .InvokeAsync<IJSObjectReference>("createFileProvider", id, fileUploaderBackend.BlazorRef)
                .ConfigureAwait(true); // Continue on Blazor context.
            webFileProviderInternal = new WebFileProviderInternal(
                webProviderInternalRef,
                fileUploaderBackend,
                true);
        }
        catch (Exception ex) {
            log.LogError(ex, "Failed to create file provider");
            fileUploaderBackend.Dispose();
            return;
        }

        var attachment = info.Attachment;
        var webFileProvider = new WebFileProvider {
            FileName = attachment.FileName,
            FileSize = attachment.Length,
            ChatId = chatId,
            WebFileProviderInternal = webFileProviderInternal,
        };

        try {
            var uploadSession = await uploadSessionManager.CreateSession(chatId, webFileProvider);
            info.UploadSession = uploadSession;
            await uploadSessionManager.ResumeSession(uploadSession.SessionId);
            ObserveUploadProgress(uploadSession, attachment);
        }
        catch (Exception ex) {
            log.LogError(ex, "Failed to create/resume upload session");
            fileUploaderBackend.Dispose();
        }
    }

    private void ObserveUploadProgress(UploadSession uploadSession, Attachment attachment)
    {
        // TODO: get rid of this hack, ProgressTracker should be able to report progress on the current thread.
        var synchronousContext = SynchronizationContext.Current;
        // Observe upload progress.
        var tracker = uploadSession.ProgressTracker;
        var id = attachment.Id;
        tracker.Progress.ProgressChanged += (_, value) => {
            synchronousContext.Post(v => {
                UpdateAttachment(id, x => x with { Progress = (int)(double)v! });
                OnChanged();
            }, value);
        };
        _ = tracker.Task.ContinueWith(t => {
            if (t.IsCompletedSuccessfully) {
                var mediaContent = t.Result;
                UpdateAttachment(id, x => x with {
                    MediaId = mediaContent.MediaId,
                    ThumbnailMediaId = mediaContent.ThumbnailMediaId,
                });
                OnChanged();
            }
            else if (t.IsFaulted) {
                UpdateAttachment(id, x => x with {
                    Failed = true,
                });
                OnChanged();
            }
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void OnChanged()
        => Changed?.Invoke(this, EventArgs.Empty);

    private ValueTask InvokeClear()
        => JSRef?.InvokeVoidAsync("clear") ?? ValueTask.CompletedTask;

    private ValueTask InvokeRemove(Attachment attachment)
        => JSRef?.InvokeVoidAsync("remove", attachment.Id) ?? ValueTask.CompletedTask;

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

    private sealed record AttachmentInfo(Attachment Attachment)
    {
        public UploadSession? UploadSession { get; set; }
    }
}
