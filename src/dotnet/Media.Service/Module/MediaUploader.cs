using System.Text;
using ActualChat.Hashing;
using ActualChat.Hosting;
using ActualChat.Media.Db;
using ActualChat.Media.Resources;
using ActualLab.Fusion.EntityFramework;
using Microsoft.AspNetCore.StaticFiles;

namespace ActualChat.Media.Module;

public sealed class MediaUploader(Type ownerType)
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

        async Task AddMedia(string id, Resource resource)
        {
            var mediaId = new MediaId(id);
            var mediaIdHash = mediaId.Hash(Encoding.UTF8).SHA256().AlphaNumeric();
            var resourceStream = resource.GetStream();
            var extension = Path.GetExtension(resource.Name);
            var type = contentTypeProvider.TryGetContentType(resource.Name, out var contentType)
                ? contentType
                : throw StandardError.Internal($"Unknown content type: {resource.Name}.");
            var contentId = $"media/{mediaIdHash}/{mediaId.LocalId}{extension}";
            var media = new Media(mediaId) {
                ContentId = contentId,
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
            if (existingMedia != null)
                existingMedia.UpdateFrom(media);
            else
                // ReSharper disable once AccessToDisposedClosure
                dbContext.Media.Add(new DbMedia(media));

            var mediaExists = await blobStorage.Exists(media.ContentId, cancellationToken).ConfigureAwait(false);
            if (mediaExists)
                await blobStorage.Delete(media.ContentId, cancellationToken).ConfigureAwait(false);
            await blobStorage.Write(media.ContentId, resourceStream, media.ContentType, cancellationToken).ConfigureAwait(false);
        }
    }

    public sealed record UploadBuilderContext(Func<string, Resource, Task> AddMedia);
}
