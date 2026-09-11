using ActualChat.UI.Blazor.App.Services;
using ActualLab.IO;

namespace ActualChat.App.Maui.Services;

public sealed class MauiProcessedImageStore(IServiceProvider services) : IProcessedImageStore
{
    public static readonly FilePath RootDirectory = new FilePath(FileSystem.CacheDirectory) | "processed-images";

    public async Task<MauiFileProvider> Save(Stream content, FileMetadata metadata, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(RootDirectory);
        var filePath = RootDirectory | ((FilePath)metadata.FileName).ToUnique();
        try {
            var file = File.Create(filePath);
            await using (file.ConfigureAwait(false))
                await content.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
        }
        catch {
            filePath.DeleteSilently();
            throw;
        }

        var fileProvider = new MauiFileProvider { FileRef = ToFileRef(filePath), Metadata = metadata };
        fileProvider.Initialize(services);
        return fileProvider;
    }

    public static bool Contains(FilePath filePath)
        => filePath.IsSubPathOf(RootDirectory);

    // Private methods

    private static FilePath ToFileRef(FilePath filePath)
#if ANDROID
        // AndroidFileProviderImpl opens file refs through ContentResolver, which takes URIs
        => Android.Net.Uri.FromFile(new Java.IO.File(filePath.Value))!.ToString()!;
#else
        => filePath;
#endif
}
