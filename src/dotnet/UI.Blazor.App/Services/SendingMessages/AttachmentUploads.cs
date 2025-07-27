namespace ActualChat.UI.Blazor.App.Services;

public sealed class AttachmentUploads
{
    private readonly TaskCompletionSource _whenUploaded = TaskCompletionSourceExt.New();

    public IAttachmentList Attachments { get; }
    public Task WhenUploaded => _whenUploaded.Task;

    public AttachmentUploads(IAttachmentList attachments)
    {
        if (attachments.Count is 0)
            throw new ArgumentException("Attachments must not be empty.", nameof(attachments));

        Attachments = attachments;
        attachments.Changed += (_, _) => ReviewState();
        ReviewState();
    }

    private void ReviewState()
    {
        var isCompleted = Attachments.Items.All(c => c.Uploaded);
        if (isCompleted)
            _whenUploaded.TrySetResult();
    }
}
