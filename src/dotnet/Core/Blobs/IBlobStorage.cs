namespace ActualChat.Blobs;

public interface IBlobStorage : IAsyncDisposable
{
    Task<Stream?> Read(
        string path,
        CancellationToken cancellationToken);

    Task<string?> GetContentType(
        string path,
        CancellationToken cancellationToken);

    Task Write(
        string path,
        Stream dataStream,
        string contentType,
        CancellationToken cancellationToken);

    Task Copy(
        string oldPath,
        string newPath,
        CancellationToken cancellationToken);

    Task Delete(
        IReadOnlyCollection<string> paths,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<bool>> Exists(
        IReadOnlyCollection<string> paths,
        CancellationToken cancellationToken);
}
