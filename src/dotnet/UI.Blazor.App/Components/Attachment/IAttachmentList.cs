namespace ActualChat.UI.Blazor.App.Components;

public interface IAttachmentList : IAsyncDisposable
{
    int Count { get; }
    IEnumerable<Attachment> Items { get; }
    event EventHandler? Changed;
    Task Remove(Attachment attachment);
}
