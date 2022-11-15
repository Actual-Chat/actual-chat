using System.Net.Mime;
using ActualChat.IO;
using Stl.IO;

namespace ActualChat.Blobs.Internal;

internal class LocalFolderBlobStorage : IBlobStorage
{
    private readonly FilePath _directory;

    public LocalFolderBlobStorage(FilePath directory)
    {
        if (directory == null)
            throw new ArgumentNullException(nameof(directory));

        _directory = directory.DirectoryPath;
    }

    public Task<Stream?> Read(string path, CancellationToken cancellationToken)
    {
        ValidatePath(path);

        try {
            return Task.FromResult<Stream?>(File.OpenRead( _directory & path));
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

        var fullPath = _directory & path;
        return !File.Exists(fullPath)
            ? Task.FromResult<string?>(null)
            : Task.FromResult<string?>(MediaTypeNames.Application.Octet);
    }

    public async Task Write(string path, Stream dataStream, string contentType, CancellationToken cancellationToken)
    {
        if (path == null) throw new ArgumentNullException(nameof(path));
        if (dataStream == null) throw new ArgumentNullException(nameof(dataStream));

        ValidatePath(path);

        var fullPath = _directory & path;
        Directory.CreateDirectory(fullPath.DirectoryPath);
        await using var fileStream = File.Create(fullPath);
        await dataStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
    }

    public Task Delete(IReadOnlyCollection<string> paths, CancellationToken cancellationToken)
    {
        if (paths == null) throw new ArgumentNullException(nameof(paths));

        if (paths.Count == 0)
            return Task.CompletedTask;

        foreach (var path in paths) {
            ValidatePath(path);

            var fullPath = (_directory & path).Value;
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

            var fullPath = (_directory & path).Value;
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
        if (!filePath.IsSubPathOf(_directory) && filePath.IsRooted)
            throw StandardError.Constraint<LocalFolderBlobStorage>("Path should be relative to the base directory.");
    }
}
