namespace ActualChat.UI.Blazor.App.Components;

public sealed class AttachmentList2 : IAttachmentList
{
    private readonly List<Attachment> _attachments;
    public int Count => _attachments.Count;
    public IEnumerable<Attachment> Items => _attachments;
    public event EventHandler? Changed;

    public AttachmentList2(IReadOnlyCollection<Attachment> attachments)
        => _attachments = attachments.ToList();

    public Task Remove(Attachment attachment)
        => throw new NotSupportedException();

    public ValueTask DisposeAsync()
        => default;
}
