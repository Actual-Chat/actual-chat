namespace ActualChat.UI.Blazor.App.Components;

public interface IAttachmentList
{
    int Count { get; }
    IEnumerable<Attachment> Items { get; }
    event EventHandler? Changed;
    Task Remove(Attachment attachment);
    Task Restart(Attachment attachment);
}
