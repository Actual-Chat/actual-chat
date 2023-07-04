namespace ActualChat.Blobs;

public interface IBlobStorage : IAsyncDisposable
{
    Task<bool> Exists(string path, CancellationToken cancellationToken);
    Task<Stream?> Read(string path, CancellationToken cancellationToken);
    Task<string?> GetContentType(string path, CancellationToken cancellationToken);
    Task Write(string path, Stream stream, string contentType, CancellationToken cancellationToken);
    Task Copy(string oldPath, string newPath, CancellationToken cancellationToken);
    Task Delete(string path, CancellationToken cancellationToken);
}
