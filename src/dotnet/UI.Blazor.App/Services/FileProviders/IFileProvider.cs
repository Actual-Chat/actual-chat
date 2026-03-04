using ActualChat.UI.App;
using ActualChat.UI.Services;

namespace ActualChat.UI.Blazor.App.Services;

#pragma warning disable MsgPack005 // Union attr required — handled by custom formatter, Maui subtype unavailable in CI

[MemoryPackable(GenerateType.NoGenerate)]
public partial interface IFileProvider
{
    FileMetadata Metadata { get; }
    Task PrepareForSaving();
    void Initialize(IServiceProvider services);
    Task<bool> CheckAccess();
    Task<bool> WhenUserConsentGranted();
    Task ClearForRemoving();
    Task<FilePreview> GetPreview(CancellationToken cancellationToken = default);
    Task WhenFileStreamReady();
    UploadSource GetUploadSource();
}

public readonly record struct FilePreview(string Url, Size? Dimensions = null);

// NOTE(DF): This is a workaround for the following issue:
// When I apply MemoryPackUnion to the interface, this is working on Desktop, but fails on Android (MAUI) with an error:
// System.BadImageFormatException: Method has no body.
[MemoryPackUnionFormatter(typeof(IFileProvider))]
[MemoryPackUnion(0, typeof(WebFileProvider))]
[MemoryPackUnion(1, typeof(MauiFileProvider))]
public partial class FileProviderUnionFormatter;
