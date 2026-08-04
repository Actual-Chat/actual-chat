using ActualChat.UI.Services;

namespace ActualChat.UI.Blazor.App.Services;

#pragma warning disable MsgPack005 // Union attr required — handled by custom formatter, Maui subtype unavailable in CI

[Union(0, typeof(WebFileProvider))]
[Union(1, typeof(MauiFileProvider))]
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

public sealed record FilePreview(string Url, Size2D? Dimensions = null);
