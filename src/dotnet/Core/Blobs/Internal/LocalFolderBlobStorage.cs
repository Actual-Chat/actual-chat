using ActualChat.IO;
using Microsoft.AspNetCore.StaticFiles;
using Stl.IO;

namespace ActualChat.Blobs.Internal;

internal class LocalFolderBlobStorage : IBlobStorage
{
    private IContentTypeProvider? _contentTypeProvider;
    private FilePath BaseDirectory { get; }
    private IServiceProvider Services { get; }

    private IContentTypeProvider ContentTypeProvider
        => _contentTypeProvider ??= Services.GetRequiredService<IContentTypeProvider>();

    public LocalFolderBlobStorage(FilePath directory, IServiceProvider services)
    {
        if (directory == null)
            throw new ArgumentNullException(nameof(directory));

        Services = services;
        BaseDirectory = directory.DirectoryPath;
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
        if (path == null) throw new ArgumentNullException(nameof(path));

        ValidatePath(path);

        var fullPath = BaseDirectory & path;
        return File.Exists(fullPath) && ContentTypeProvider.TryGetContentType(fullPath, out var contentType)
            ? Task.FromResult<string?>(contentType)
            : Task.FromResult<string?>(null);
    }

    public async Task Write(string path, Stream dataStream, string contentType, CancellationToken cancellationToken)
    {
        if (path == null) throw new ArgumentNullException(nameof(path));
        if (dataStream == null) throw new ArgumentNullException(nameof(dataStream));

        ValidatePath(path);

        var fullPath = BaseDirectory & path;
        Directory.CreateDirectory(fullPath.DirectoryPath);
        var fileStream = File.Create(fullPath);
        await using var _ = fileStream.ConfigureAwait(false);
        await dataStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
    }

    public Task Copy(string oldPath, string newPath, CancellationToken cancellationToken)
    {
        if (oldPath == null) throw new ArgumentNullException(nameof(oldPath));
        if (newPath == null) throw new ArgumentNullException(nameof(newPath));

        ValidatePath(oldPath);
        ValidatePath(newPath);

        var fullOldPath = BaseDirectory & oldPath;
        var fullNewPath = BaseDirectory & newPath;

        Directory.CreateDirectory(fullNewPath.DirectoryPath);

        File.Copy(fullOldPath, fullNewPath);

        return Task.CompletedTask;
    }

    public Task Delete(IReadOnlyCollection<string> paths, CancellationToken cancellationToken)
    {
        if (paths == null) throw new ArgumentNullException(nameof(paths));

        if (paths.Count == 0)
            return Task.CompletedTask;

        foreach (var path in paths) {
            ValidatePath(path);

            var fullPath = (BaseDirectory & path).Value;
            if (File.Exists(fullPath))
                File.Delete(fullPath);
            else if (Directory.Exists(fullPath))
                Directory.Delete(fullPath, true);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<bool>> Exists(
        IReadOnlyCollection<string> paths,
        CancellationToken cancellationToken)
    {
        if (paths == null) throw new ArgumentNullException(nameof(paths));

        if (paths.Count == 0)
            return Task.FromResult<IReadOnlyCollection<bool>>(Array.Empty<bool>());

        var index = 0;
        var result = new bool[paths.Count];
        foreach (var path in paths) {
            ValidatePath(path);

            var fullPath = (BaseDirectory & path).Value;
            if (File.Exists(fullPath))
                result[index] = true;
            else if (Directory.Exists(fullPath))
                result[index] = true;
            index++;
        }
        return Task.FromResult<IReadOnlyCollection<bool>>(result);
    }

    public ValueTask DisposeAsync()
        => ValueTask.CompletedTask;

    private void ValidatePath(string path)
    {
        if (path == null) throw new ArgumentNullException(nameof(path));

        var filePath = FilePath.New(path);
        if (!filePath.IsSubPathOf(BaseDirectory) && filePath.IsRooted)
            throw StandardError.Constraint<LocalFolderBlobStorage>("Path should be relative to the base directory.");
    }
}
