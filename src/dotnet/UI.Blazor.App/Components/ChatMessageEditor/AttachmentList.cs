using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public sealed class AttachmentList : IAttachmentList
{
    private ImmutableList<Attachment> _attachments = ImmutableList<Attachment>.Empty;
    private IAttachmentListEventsListener? _listener;

    public int Count => _attachments.Count;
    public IEnumerable<Attachment> Items => _attachments;
    public ImageQualityPreset ImageQuality { get; private set; }
    public bool HasReEncodableImages => _attachments.Any(a => a.IsReEncodable);
    // Set once the user shows commitment - by typing, picking a preset, or sending; until then
    // an attachment costs nothing, so nothing about it is encoded or uploaded
    public bool IsCommitted { get; private set; }
    public event EventHandler? Changed;
    public string MediaScope { get; init; } = "";

    public void Commit()
        => IsCommitted = true;

    public void SetImageQuality(ImageQualityPreset preset)
    {
        ImageQuality = preset;
        RaiseChanged();
    }

    public void Add(Attachment attachment)
    {
        _attachments = _attachments.Add(attachment);
        RaiseChanged();
    }

    public void Replace(Attachment oldAttachment, Attachment newAttachment)
    {
        var index = _attachments.IndexOf(oldAttachment);
        if (index < 0)
            throw StandardError.Internal("Attachment not found.");

        _attachments = _attachments.SetItem(index, newAttachment);
        RaiseChanged();
    }

    public async Task Remove(Attachment attachment)
    {
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

    // Private methods

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
