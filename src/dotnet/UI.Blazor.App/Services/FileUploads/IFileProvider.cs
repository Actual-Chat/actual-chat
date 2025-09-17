using MemoryPack;

namespace ActualChat.UI.Blazor.App.Services;

[MemoryPackable(GenerateType.NoGenerate)]
public partial interface IFileProvider
{
    string FileName { get; }
    Task PrepareForSaving();
    Task<IFileUploadOperation> CreateUploadOperation();
    void Initialize(IServiceProvider services);
    Task<bool> CheckAccess();
    Task ClearBeforeRemoving();
    Task<string> GetPreviewUrl();
}

// NOTE(DF): This is a workaround for the following issue:
// When I apply MemoryPackUnion to the interface, this is working on Desktop, but fails on Android (MAUI) with an error:
// System.BadImageFormatException: Method has no body.
[MemoryPackUnionFormatter(typeof(IFileProvider))]
[MemoryPackUnion(0, typeof(WebFileProvider))]
[MemoryPackUnion(1, typeof(IncomingShareFileProvider))]
[MemoryPackUnion(2, typeof(LocalFileProvider))]
public partial class FileProviderUnionFormatter;
