using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public class AttachmentList(UploadSessions uploadSessions) : IAttachmentList
{
    public static Exception FileTooBigError()
        => StandardError.Constraint($"File is too big. Max file size: {Constants.Attachments.FileSizeLimit / 1024 / 1024}Mb.");

    private ImmutableList<Attachment> _attachments = ImmutableList<Attachment>.Empty;

    public int Count => _attachments.Count;
    public IEnumerable<Attachment> Items => _attachments;
    public event EventHandler? Changed;

    public async Task Remove(Attachment attachment) {
        if (!_attachments.Contains(attachment))
            throw StandardError.Internal("Attachment not found.");
        _attachments = _attachments.Remove(attachment);
        await CancelAndDisposeAttachment(attachment);
        OnChanged();
    }

    public async Task Clear()
    {
        var clone = _attachments;
        _attachments = _attachments.Clear();
        await clone.Select(CancelAndDisposeAttachment).Collect();
        OnChanged();
    }

    private async Task CancelAndDisposeAttachment(Attachment a)
    {
        if (!a.UploadSessionId.IsNullOrEmpty())
            await uploadSessions.CancelSession(a.UploadSessionId).ConfigureAwait(false);
        if (a.FileProvider is null)
            return;

        await a.FileProvider.ClearBeforeRemoving();
        if (a.FileProvider is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeSilentlyAsync();
    }

    private void OnChanged()
        => Changed?.Invoke(this, EventArgs.Empty);

    public void UpdateAttachment(string id, Func<Attachment, Attachment> updater) {
        var i = _attachments.FindIndex(x => OrdinalEquals(x.Id, id));
        if (i < 0)
            return;

        var attachment = _attachments[i];
        _attachments = _attachments.SetItem(i, updater(attachment));
        OnChanged();
    }

    public Exception? CheckCanAdd(long length)
    {
        if (length > Constants.Attachments.FileSizeLimit)
            return FileTooBigError();

        if (_attachments.Count >= Constants.Attachments.FileCountLimit)
            return StandardError.Constraint("Too many files. Max allowed number is 10.");

        return null;
    }

    public void Add(Attachment attachment)
    {
        _attachments = _attachments.Add(attachment);
        OnChanged();
    }

    public ValueTask DisposeAsync()
        => default;
}
