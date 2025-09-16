namespace ActualChat.UI.Blazor.App.Components;

public sealed class AttachmentWebFilePickerBackend : IAttachmentWebFilePickerBackend, IDisposable
{
    private readonly Func<FileInfo, Task<bool>> _onFilePicked;
    private readonly DotNetObjectReference<IAttachmentWebFilePickerBackend> _blazorRef;
    private bool _disposed;

    public DotNetObjectReference<IAttachmentWebFilePickerBackend> BlazorRef {
        get {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AttachmentWebFilePickerBackend));
            return _blazorRef;
        }
    }

    public AttachmentWebFilePickerBackend(Func<FileInfo, Task<bool>> onFilePicked)
    {
        _onFilePicked = onFilePicked;
        _blazorRef = DotNetObjectReference.Create<IAttachmentWebFilePickerBackend>(this);
    }

    [JSInvokable]
    public Task<bool> OnFilePicked(int id, string? fileName, string? fileType, int length)
        => _onFilePicked(new FileInfo(id, fileName ?? "", fileType ?? "", length));

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _blazorRef.DisposeSilently();
    }

    // Nested types
    public record FileInfo(int Id, string FileName, string FileType, int Length);
}
