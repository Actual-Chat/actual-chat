using System.Text;

namespace ActualChat.Media;

public class UploadsStorage(IServiceProvider services)
{
    protected IServiceProvider Services { get; } = services;
    [field:AllowNull, MaybeNull]
    private IBlobStorages Blobs => field ??= Services.GetRequiredService<IBlobStorages>();
    private IBlobStorage BlobStorage => Blobs[BlobScope.UploadTempRecord];

    public async Task<long?> GetUploadOffset(UploadId uploadId, CancellationToken cancellationToken = default)
    {
        var stream = await BlobStorage.Read(GetDataFileId(uploadId), cancellationToken).ConfigureAwait(false);
        if (stream is null)
            return null;

        await using (stream.ConfigureAwait(false))
            return stream.Length;
    }

    public async Task AppendDataAsync(UploadId uploadId, Stream stream, CancellationToken cancellationToken)
        => await BlobStorage.Append(GetDataFileId(uploadId), stream, cancellationToken).ConfigureAwait(false);

    public Task CreateEmptyDataFile(UploadId uploadId, string contentType, CancellationToken cancellationToken)
        => CreateFile(GetDataFileId(uploadId), Stream.Null, contentType, cancellationToken);

    public async Task CreateMetadataFile(UploadId uploadId, string json, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        var stream = MemoryStreamManager.Default.GetStream(nameof(Uploads), bytes.Length);
        await using (stream.ConfigureAwait(false)) {
            stream.Write(bytes);
            stream.Position = 0;
            await CreateFile(GetMetadataFileId(uploadId), stream, "application/json", cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task<string?> GetMetadataFile(UploadId uploadId, CancellationToken cancellationToken)
    {
        var stream = await BlobStorage.Read(GetMetadataFileId(uploadId), cancellationToken).ConfigureAwait(false);
        if (stream == null)
            return null;

        using var reader = new StreamReader(stream, Encoding.UTF8);
        var json = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        return json;
    }

    public async Task DeleteFiles(UploadId uploadId, CancellationToken cancellationToken)
    {
        await BlobStorage.DeleteIfExists(GetDataFileId(uploadId), cancellationToken).ConfigureAwait(false);
        await BlobStorage.DeleteIfExists(GetMetadataFileId(uploadId), cancellationToken).ConfigureAwait(false);
    }

    public async Task<Stream> GetDataFile(UploadId uploadId, CancellationToken cancellationToken)
        => await BlobStorage.Read(GetDataFileId(uploadId), cancellationToken).Require().ConfigureAwait(false);

    private Task CreateFile(string fileId, Stream stream, string contentType, CancellationToken cancellationToken)
        => BlobStorage.Write(fileId, stream, contentType, cancellationToken);

    private static string GetDataFileId(UploadId uploadId)
        => GetPath(uploadId.Value);

    private static string GetMetadataFileId(UploadId uploadId)
        => GetPath(uploadId.Value + ".metadata");

    private static string GetPath(string localFieldId)
        => "upload-temp/" + localFieldId;
}
