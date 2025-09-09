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
    Task<IFileUploadOperation> CreateUploadOperation();
    void Initialize(IServiceProvider services);
    Task<bool> CheckAccess();
    Task ClearBeforeRemoving();
}
