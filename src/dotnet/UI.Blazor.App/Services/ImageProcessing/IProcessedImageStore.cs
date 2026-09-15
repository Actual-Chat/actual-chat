namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Stores a processed attachment image as a local file, so native uploads (and their resume
/// after an app restart) work the same as for picked files. Implemented by the MAUI host.
/// </summary>
public interface IProcessedImageStore
{
    Task<MauiFileProvider> Save(Stream content, FileMetadata metadata, CancellationToken cancellationToken);
}
