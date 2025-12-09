using MemoryPack;

namespace ActualChat.UI.Blazor.App.Services;

[MemoryPackable(GenerateType.NoGenerate)]
public partial interface IFileProvider
{
    FileMetadata Metadata { get; }
    Task PrepareForSaving();
    void Initialize(IServiceProvider services);
    Task<bool> CheckAccess();
    Task<bool> WhenUserConsentGranted();
    Task ClearForRemoving();
    Task<string> GetPreviewUrl();
    Task WhenFileStreamReady();
    Task<Result<Unit>> UploadData(UploadId uploadId, IProgress<double> progressTracker, CancellationToken ct);
}

// NOTE(DF): This is a workaround for the following issue:
// When I apply MemoryPackUnion to the interface, this is working on Desktop, but fails on Android (MAUI) with an error:
// System.BadImageFormatException: Method has no body.
[MemoryPackUnionFormatter(typeof(IFileProvider))]
[MemoryPackUnion(0, typeof(WebFileProvider))]
[MemoryPackUnion(1, typeof(MauiFileProvider))]
public partial class FileProviderUnionFormatter;
