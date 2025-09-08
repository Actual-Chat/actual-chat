using MemoryPack;

namespace ActualChat.UI.Blazor.App.Services;

[MemoryPackable]
[MemoryPackUnion(0, typeof(WebFileProvider))]
// [MemoryPackUnion(1, typeof(LocalFileProvider))]
public partial interface IFileProvider
{
    string FileName { get; }
    long FileSize { get; }
    Task PrepareForSaving();
    Task ClearBeforeRemoving();
    Task<IFileUploadOperation> CreateUploadOperation();
    Task<bool> CheckAccess(UploadSessionContext context);
}
