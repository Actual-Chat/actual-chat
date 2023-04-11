using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Services;

public class ChatAttachmentsUI : IComputeService
{
    private readonly object _lock = new ();
    private readonly List<Attachment> _editorAttachments = new ();
    private readonly Dictionary<int, Attachment> _uploading = new ();

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
        }

        InvalidateEditorAttachments();

        return true;
    }

    public void UpdateProgress(int id, int progress)
    {
        lock (_lock) {
            var uploading = _uploading[id]?.WithProgress(progress);
            if (uploading is not null)
                _uploading[id] = uploading;

            var idx = _editorAttachments.FindIndex(x => x.Id == id);
            if (idx >= 0) {
                _editorAttachments[idx] = _editorAttachments[idx].WithProgress(progress);
                // TODO: optimize - update only single item
                InvalidateEditorAttachments();
            }
        }
    }

    [ComputeMethod]
    public ImmutableArray<Attachment> GetEditorAttachments(CancellationToken cancellationToken = default)
    {
        lock (_lock)
            return _editorAttachments.ToImmutableArray();
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

    private void InvalidateEditorAttachments()
    {
        using (Computed.Invalidate())
            _ = GetEditorAttachments(default);
    }
}
