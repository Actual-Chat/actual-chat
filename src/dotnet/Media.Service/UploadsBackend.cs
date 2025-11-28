using System.Text;

namespace ActualChat.Media;

public class UploadsStorage(IServiceProvider services)
{
    protected IServiceProvider Services { get; } = services;
    [field:AllowNull, MaybeNull]
    private IBlobStorages Blobs => field ??= Services.GetRequiredService<IBlobStorages>();
    private IBlobStorage BlobStorage => Blobs[BlobScope.UploadTempRecord];

    public async Task<long> GetUploadOffset(UploadId uploadId, CancellationToken cancellationToken = default)
    {
        var stream = await BlobStorage.Read(GetDataFileId(uploadId), cancellationToken).ConfigureAwait(false);
        if (stream is null)
            return -1;

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

public class UploadsBackend(IServiceProvider services) : IUploadsBackend
{
    private UploadsStorage UploadsStorage { get; } = services.GetRequiredService<UploadsStorage>();

    public virtual async Task<Upload?> Get(UploadId uploadId, CancellationToken cancellationToken)
    {
        var json = await UploadsStorage.GetMetadataFile(uploadId, cancellationToken).ConfigureAwait(false);
        return json.IsNullOrEmpty() ? null : JsonSerializer.Deserialize<Upload>(json);
    }

    public virtual async Task<UploadId> OnCreate(UploadsBackend_Create command, CancellationToken cancellationToken)
    {
        var uploadId = UploadId.New();
        var upload = new Upload(uploadId, command.UserId, command.Length, command.Tag, command.Metadata);
        var contentType = upload.ContentType.NullIfEmpty() ?? "application/octet-stream";
        var json = JsonSerializer.Serialize(upload);
        await UploadsStorage.CreateMetadataFile(uploadId, json, cancellationToken).ConfigureAwait(false);
        await UploadsStorage.CreateEmptyDataFile(uploadId,  contentType, cancellationToken).ConfigureAwait(false);

        InvalidateUpload(uploadId);
        return uploadId;
    }

    public virtual async Task OnRemove(UploadsBackend_Remove command, CancellationToken cancellationToken)
    {
        var uploadId = command.Id;
        await UploadsStorage.DeleteFiles(uploadId, cancellationToken).ConfigureAwait(false);
        InvalidateUpload(uploadId);
    }

    private void InvalidateUpload(UploadId uploadId)
    {
        using (Invalidation.Begin())
            _ = Get(uploadId, default);
    }
}
