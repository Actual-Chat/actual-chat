using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Services;

public class ChatAttachmentsUI : IComputeService
{
    private readonly object _lock = new ();
    // TODO: consider chat id
    private readonly List<Attachment> _editorAttachments = new ();
    private readonly Dictionary<Key, Attachment> _uploading = new ();

    private ErrorUI ErrorUI { get; }

    public ChatAttachmentsUI(IServiceProvider services)
        => ErrorUI = services.GetRequiredService<ErrorUI>();

    public bool Add(Attachment attachment)
    {
        lock (_lock) {
            if (attachment.Length > Constants.Attachments.FileSizeLimit) {
                ErrorUI.ShowError("File is too big. Max file size: 8Mb.");
                return false;
            }
            if (_editorAttachments.Count >= Constants.Attachments.FileCountLimit) {
                ErrorUI.ShowError("Too many files. Max allowed number is 10.");
                return false;
            }

            _editorAttachments.Add(attachment);
            _uploading.Add(new (attachment.ChatId, attachment.Id), attachment);
        }

        InvalidateEditorAttachments();

        return true;
    }

    public void UpdateProgress(ChatId chatId, int id, int progress)
    {
        lock (_lock) {
            var key = new Key(chatId, id);
            var uploading = _uploading[key]?.WithProgress(progress);
            if (uploading is not null)
                _uploading[key] = uploading;

            var idx = _editorAttachments.FindIndex(x => x.Id == id);
            if (idx >= 0) {
                _editorAttachments[idx] = _editorAttachments[idx].WithProgress(progress);
                // TODO: optimize - update only single item
                InvalidateEditorAttachments();
            }
        }
    }

    public void CompleteUpload(ChatId chatId, int id, MediaId mediaId)
    {
        lock (_lock) {
            var key = new Key(chatId, id);
            var uploading = _uploading[key]?.WithMediaId(mediaId);
            if (uploading is not null)
                _uploading[key] = uploading;

            // TODO: not nice that _editorAttachments and _uploading duplicate some logic
            var idx = _editorAttachments.FindIndex(x => x.Id == id);
            if (idx >= 0) {
                _editorAttachments[idx] = _editorAttachments[idx].WithMediaId(mediaId);
                // TODO: optimize - update only single item
                InvalidateEditorAttachments();
            }
        }
    }

    [ComputeMethod]
    public virtual Task<ImmutableArray<Attachment>> GetEditorAttachments(CancellationToken cancellationToken = default)
    {
        // todo:
        lock (_lock)
            return Task.FromResult(_editorAttachments.ToImmutableArray());
    }

    public ImmutableArray<Attachment> PopAll()
    {
        ImmutableArray<Attachment> result;
        lock (_lock) {
            result = _editorAttachments.ToImmutableArray();
            _editorAttachments.Clear();
        }
        InvalidateEditorAttachments();
        return result;
    }

    public void RestoreEditorAttachments(ImmutableArray<Attachment> attachments)
    {
        lock (_lock)
            _editorAttachments.AddRange(attachments);
        InvalidateEditorAttachments();
    }

    // TODO: consider chatId
    public void ClearEditorAttachments()
    {
        lock (_lock)
            _editorAttachments.Clear();
        InvalidateEditorAttachments();
    }

    public void Remove(Attachment attachment)
    {
        lock (_lock) {
            _editorAttachments.RemoveAll(x => x.Id == attachment.Id);
            _uploading.Remove(new (attachment.ChatId, attachment.Id));
        }
        InvalidateEditorAttachments();
    }

    private void InvalidateEditorAttachments()
    {
        using (Computed.Invalidate())
            _ = GetEditorAttachments(default);
    }

    private record Key(ChatId ChatId, int Lid);
}
