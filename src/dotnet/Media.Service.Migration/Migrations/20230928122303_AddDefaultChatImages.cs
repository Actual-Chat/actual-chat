using ActualChat.Db;
using ActualChat.Hosting;
using ActualChat.Media.Db;
using ActualChat.Media.Module;
using ActualChat.Media.Resources;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using ActualLab.Fusion.EntityFramework;

#nullable disable
#pragma warning disable VSTHRD002

namespace ActualChat.Media.Migrations
{
    /// <inheritdoc />
    public partial class AddDefaultChatImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            UpAsync(migrationBuilder).Wait();
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        { }

        private async Task UpAsync(MigrationBuilder migrationBuilder)
        {
            var contentTypeProvider = new FileExtensionContentTypeProvider();
            var dbInitializer = DbInitializer.GetCurrent<MediaDbInitializer>();
            var log = dbInitializer.Services.LogFor(GetType());

            var blobStorageProvider = dbInitializer.Services.GetRequiredService<IBlobStorageProvider>();
            var blobStorage = blobStorageProvider.GetBlobStorage(BlobScope.ContentRecord);

            using var dbContext = dbInitializer.CreateDbContext(true);

            log.LogInformation("Uploading chat pictures");

            await AddMedia("system-icons:family", Resource.FamilySvg).ConfigureAwait(false);
            await AddMedia("system-icons:coworkers", Resource.CoworkersSvg).ConfigureAwait(false);
            await AddMedia("system-icons:friends", Resource.FriendsSvg).ConfigureAwait(false);
            await AddMedia("system-icons:alumni", Resource.AlumniSvg).ConfigureAwait(false);
            await AddMedia("system-icons:notes", Resource.NotesSvg).ConfigureAwait(false);

            await dbContext.SaveChangesAsync().ConfigureAwait(false);

            log.LogInformation("Uploading chat pictures: done");

            async Task AddMedia(string id, Resource resource) {
                var mediaId = new MediaId(id);
                var hashCode = mediaId.Id.ToString().GetSHA256HashCode(HashEncoding.AlphaNumeric);
                var resourceStream = resource.GetStream();
                var extension = Path.GetExtension(resource.Name);
                var type = contentTypeProvider.TryGetContentType(resource.Name, out var contentType)
                    ? contentType
                    : throw StandardError.Internal($"Unknown content type: {resource.Name}.");
                var contentId = $"media/{hashCode}/{mediaId.LocalId}{extension}";
                var media = new Media(mediaId) {
                    ContentId = contentId,
                    FileName = resource.Name,
                    Length = resourceStream.Length,
                    ContentType = type,
                    Width = 0,
                    Height = 0,
                };

                var existingMedia = await dbContext.Media
                    .FindAsync(DbKey.Compose(mediaId.Value))
                    .ConfigureAwait(false);
                if (existingMedia != null)
                    existingMedia.UpdateFrom(media);
                else
                    dbContext.Media.Add(new DbMedia(media));

                var mediaExists = await blobStorage.Exists(media.ContentId, default).ConfigureAwait(false);
                if (mediaExists) {
                    await blobStorage.Delete(media.ContentId, default).ConfigureAwait(false);
                }
                await blobStorage.Write(media.ContentId, resourceStream, media.ContentType, default).ConfigureAwait(false);
            }
        }
    }
}
