using Microsoft.AspNetCore.StaticFiles;
using Stl.IO;

namespace ActualChat.Blobs.Internal;

internal class LocalFolderBlobStorage(LocalFolderBlobStorage.Options options, IServiceProvider services)
    : IBlobStorage
{
    public record Options
    {
        public FilePath BaseDirectory { get; init; } = ".";
    }

    private IContentTypeProvider? _contentTypeProvider;
    private FilePath BaseDirectory { get; } = options.BaseDirectory.FullPath.DirectoryPath;
    private IServiceProvider Services { get; } = services;

    private IContentTypeProvider ContentTypeProvider
        => _contentTypeProvider ??= Services.GetRequiredService<IContentTypeProvider>();

    public ValueTask DisposeAsync()
        => default;

    public Task<bool> Exists(string path, CancellationToken cancellationToken)
    {
        ValidatePath(path);

        var fullPath = (BaseDirectory & path).Value;
        if (File.Exists(fullPath))
            return Stl.Async.TaskExt.TrueTask;
        if (Directory.Exists(fullPath))
            return Stl.Async.TaskExt.TrueTask;

        return Stl.Async.TaskExt.FalseTask;
    }

    public Task<Stream?> Read(string path, CancellationToken cancellationToken)
    {
        ValidatePath(path);

        try {
            return Task.FromResult<Stream?>(File.OpenRead( BaseDirectory & path));
        }
        catch (DirectoryNotFoundException) {
            return Task.FromResult<Stream?>(null);
        }
        catch (FileNotFoundException) {
            return Task.FromResult<Stream?>(null);
        }
    }

    public Task<string?> GetContentType(string path, CancellationToken cancellationToken)
    {
        ValidatePath(path);

        var fullPath = BaseDirectory & path;
        return File.Exists(fullPath) && ContentTypeProvider.TryGetContentType(fullPath, out var contentType)
            ? Task.FromResult<string?>(contentType)
            : Task.FromResult<string?>(null);
    }

    public async Task Write(string path, Stream stream, string contentType, CancellationToken cancellationToken)
    {
        ValidatePath(path);

        var fullPath = BaseDirectory & path;
        Directory.CreateDirectory(fullPath.DirectoryPath);
        var fileStream = File.Create(fullPath);
        await using var _ = fileStream.ConfigureAwait(false);
        await stream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
    }

    public Task Copy(string oldPath, string newPath, CancellationToken cancellationToken)
    {
        ValidatePath(oldPath);
        ValidatePath(newPath);

        var fullOldPath = BaseDirectory & oldPath;
        var fullNewPath = BaseDirectory & newPath;

        Directory.CreateDirectory(fullNewPath.DirectoryPath);

        File.Copy(fullOldPath, fullNewPath);

        return Task.CompletedTask;
    }

    public Task Delete(string path, CancellationToken cancellationToken)
    {
        ValidatePath(path);

        var fullPath = (BaseDirectory & path).Value;
        if (File.Exists(fullPath))
            File.Delete(fullPath);
        else if (Directory.Exists(fullPath))
            Directory.Delete(fullPath, true);

        return Task.CompletedTask;
    }

    // Private methods

    private void ValidatePath(string path)
    {
        if (path == null)
            throw new ArgumentNullException(nameof(path));

        var filePath = FilePath.New(path);
        if (filePath.IsRooted && !filePath.IsSubPathOf(BaseDirectory))
            throw StandardError.Constraint<LocalFolderBlobStorage>(
                "Path should be either relative to the base directory or rooted there.");
    }
}
