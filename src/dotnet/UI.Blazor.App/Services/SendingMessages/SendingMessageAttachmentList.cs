namespace ActualChat.UI.Blazor.App.Services;

public class SendingMessageAttachmentList : IAttachmentList
{
    private readonly UploadSessionManager _uploadSessionManager;
    private Attachment[] _attachments;

    private SendingMessageAttachmentList(Attachment[] attachments, UploadSessionManager uploadSessionManager)
    {
        _attachments = attachments;
        _uploadSessionManager = uploadSessionManager;
    }

    public static async Task<SendingMessageAttachmentList> Create(Dispatcher dispatcher, UploadSessionManager uploadSessionManager, Attachment[] attachments)
    {
        var list = new SendingMessageAttachmentList(attachments, uploadSessionManager);

        foreach (var attachment in attachments) {
            var sessionId = attachment.UploadSessionId;
            if (sessionId.IsNullOrEmpty())
                throw new InvalidOperationException($"Can not arrange upload for attachment '{attachment.Id}' because no upload session specified");
            var uploadSession = await uploadSessionManager.GetSession(sessionId).ConfigureAwait(false);
            AttachmentExt.ObserveUploadProgress(
                uploadSession.ProgressTracker,
                updater => {
                    _ = dispatcher.InvokeAsync(() => {
                        list.UpdateAttachment(attachment.Id, updater);
                        list.OnChanged();
                    });
                });
        }
        return list;
    }

    private void UpdateAttachment(int attachmentId, Func<Attachment, Attachment> updater)
    {
        var attachment = _attachments.FirstOrDefault(x => x.Id == attachmentId);
        if (attachment is null)
            throw new InvalidOperationException($"Can not find attachment with id '{attachmentId}'");

        var i = _attachments.IndexOf(attachment);
        _attachments[i] = updater(attachment);
    }

    public int Count => _attachments.Length;

    public IEnumerable<Attachment> Items => _attachments;

    public event EventHandler? Changed;

    public async Task Remove(Attachment attachment)
    {
        var i = _attachments.IndexOf(attachment);
        if (i < 0)
            throw new InvalidOperationException($"Can not find given attachment.");

        _attachments = _attachments.Where(c => c != attachment).ToArray();
        await _uploadSessionManager.CancelSession(attachment.UploadSessionId).ConfigureAwait(false);
        await _uploadSessionManager.DeleteSession(attachment.UploadSessionId).ConfigureAwait(false);
    }

    private void OnChanged()
        => Changed?.Invoke(this, EventArgs.Empty);

    public ValueTask DisposeAsync()
        => default;
}
