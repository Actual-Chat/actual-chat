using ActualChat.Media.Db;
using ActualChat.Media.Resources;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Versioning;
using Microsoft.AspNetCore.StaticFiles;

namespace ActualChat.Media.Module;

public sealed class MediaUploader(Type ownerType, VersionGenerator<long> versionGenerator)
{
    public async Task Upload(Func<UploadBuilderContext, Task> uploadBuilder, CancellationToken cancellationToken)
    {
        var contentTypeProvider = new FileExtensionContentTypeProvider();
        var dbInitializer = DbInitializer.GetCurrent<MediaDbInitializer>();
        var services = dbInitializer.Services;
        var log = services.LogFor(ownerType);

        var blobStorageProvider = services.GetRequiredService<IBlobStorages>();
        var blobStorage = blobStorageProvider[BlobScope.ContentRecord];

        var dbContext = dbInitializer.CreateDbContext(true);
        await using var _ = dbContext.ConfigureAwait(false);

        log.LogInformation("Uploading chat pictures");
        var uploadBuilderContext = new UploadBuilderContext(AddMedia);
        await uploadBuilder.Invoke(uploadBuilderContext).ConfigureAwait(false);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        log.LogInformation("Uploading chat pictures: done");
        return;

        async Task AddMedia(string id, Resource resource, MediaKind kind)
        {
            var mediaId = MediaId.Parse(id);
            var resourceStream = resource.GetStream();
            var extension = Path.GetExtension(resource.Name);
            var type = contentTypeProvider.TryGetContentType(resource.Name, out var contentType)
                ? contentType
                : throw StandardError.Internal($"Unknown content type: {resource.Name}.");
            var blobId = MediaSaver.GetBlobId(mediaId, extension);
            var media = new MediaFull(mediaId) {
                BlobId = blobId,
                Kind = kind,
                FileName = resource.Name,
                Length = resourceStream.Length,
                ContentType = type,
                Width = 0,
                Height = 0,
            };

            // ReSharper disable once AccessToDisposedClosure
            var existingMedia = await dbContext.Media
                .FindAsync(DbKey.Compose(mediaId.Value), cancellationToken)
                .ConfigureAwait(false);
            if (existingMedia != null) {
                media = media with {
                    Version = versionGenerator.NextVersion(existingMedia.Version),
                };
                existingMedia.UpdateFrom(media);
            }
            else {
                media = media with { Version = versionGenerator.NextVersion() };
                // ReSharper disable once AccessToDisposedClosure
                dbContext.Media.Add(new DbMedia(media));
            }

            var mediaExists = await blobStorage.Exists(media.BlobId, cancellationToken).ConfigureAwait(false);
            if (mediaExists)
                await blobStorage.Delete(media.BlobId, cancellationToken).ConfigureAwait(false);
            await blobStorage.Write(media.BlobId, resourceStream, media.ContentType, cancellationToken).ConfigureAwait(false);
        }
    }

    public sealed record UploadBuilderContext(Func<string, Resource, MediaKind, Task> AddMedia);
}
