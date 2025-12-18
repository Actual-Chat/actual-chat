namespace ActualChat.UI.Blazor.App.Components;

public class AttachmentList : IAttachmentList
{
    public static Exception FileTooBigError()
        => StandardError.Constraint($"File is too big. Max file size: {Constants.Attachments.FileSizeLimit / 1024 / 1024}Mb.");

    private ImmutableList<Attachment> _attachments = ImmutableList<Attachment>.Empty;
    private IAttachmentListEventsListener? _listener;

    public int Count => _attachments.Count;
    public IEnumerable<Attachment> Items => _attachments;
    public event EventHandler? Changed;

    public void Add(Attachment attachment)
    {
        _attachments = _attachments.Add(attachment);
        RaiseChanged();
    }

    public void UpdateAttachment(string id, Func<Attachment, Attachment> updater) {
        var i = _attachments.FindIndex(x => OrdinalEquals(x.Id, id));
        if (i < 0)
            return;

        var attachment = _attachments[i];
        _attachments = _attachments.SetItem(i, updater(attachment));
        RaiseChanged();
    }

    public async Task Remove(Attachment attachment) {
        EnsureBelongsToList(attachment);
        _attachments = _attachments.Remove(attachment);
        RaiseChanged();
        await RaiseAttachmentsRemoved([attachment]);
    }

    public async Task Restart(Attachment attachment)
    {
        EnsureBelongsToList(attachment);
        await RaiseRestartUploadRequested(attachment);
    }

    public async Task Clear()
    {
        if (_attachments.IsEmpty)
            return;
        var clone = _attachments.ToArray();
        _attachments = _attachments.Clear();
        await RaiseAttachmentsRemoved(clone);
        RaiseChanged();
    }

    public void Subscribe(IAttachmentListEventsListener listener)
    {
        if (_listener != null && _listener != listener)
            throw StandardError.Constraint("Already subscribed.");
        _listener = listener;
    }

    private void EnsureBelongsToList(Attachment attachment)
    {
        if (!_attachments.Contains(attachment))
            throw StandardError.Internal("Attachment not found.");
    }

    private Task RaiseAttachmentsRemoved(Attachment[] attachments)
        => _listener?.AttachmentsRemoved(this, attachments) ?? Task.CompletedTask;

    private Task RaiseRestartUploadRequested(Attachment attachment)
        => _listener?.RestartUploadRequested(this, attachment) ?? Task.CompletedTask;

    private void RaiseChanged()
        => Changed?.Invoke(this, EventArgs.Empty);
}
