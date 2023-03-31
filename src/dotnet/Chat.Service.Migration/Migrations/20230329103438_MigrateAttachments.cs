using ActualChat.Chat.Module;
using ActualChat.Db;
using ActualChat.Hosting;
using ActualChat.Media.Db;
using ActualChat.Media.Module;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CS0618
#pragma warning disable VSTHRD002

namespace ActualChat.Chat.Migrations
{
    /// <inheritdoc />
    public partial class MigrateAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            UpAsync(migrationBuilder).Wait();
        }

        private async Task UpAsync(MigrationBuilder migrationBuilder)
        {
            var dbInitializer = DbInitializer.Get<ChatDbInitializer>();
            var mediaDbInitializer = await DbInitializer.Get<MediaDbInitializer>()
                .CompleteEarlierMigrations(this)
                .ConfigureAwait(false);
            var log = dbInitializer.Services.LogFor(GetType());
            var blobStorageProvider = dbInitializer.Services.GetRequiredService<IBlobStorageProvider>();
            var blobStorage = blobStorageProvider.GetBlobStorage(BlobScope.ContentRecord);

            var skip = 0;
            const int take = 100;
            var blobs = new List<(string oldPath, string newPath)>(take);

            while (true) {
                using var dbContext = dbInitializer.DbHub.CreateDbContext(true);
                using var mediaDbContext = mediaDbInitializer.DbHub.CreateDbContext(true);
                mediaDbContext.ChangeTracker.AutoDetectChangesEnabled = false;

                var dbAttachments = await dbContext.TextEntryAttachments
                    .Where(x => x.ContentId != "")
                    .OrderBy(c => c.Id)
                    .Skip(skip)
                    .Take(take)
                    .ToListAsync()
                    .ConfigureAwait(false);

                if (!dbAttachments.Any())
                    break;

                log.LogInformation("Upgrading {Count} attachments", dbAttachments.Count);

                foreach (var dbAttachment in dbAttachments) {
                    var attachment = dbAttachment.ToModel();
                    var mediaId = new MediaId(attachment.ChatId, Generate.Option);
                    var hashCode = mediaId.Id.ToString().GetSHA256HashCode();
                    var media = new Media.Media {
                        Id = mediaId,
                        MetadataJson = dbAttachment.MetadataJson,
                    };

                    media = media with {
                        ContentId = $"media/{hashCode}/{mediaId.LocalId}{Path.GetExtension(media.FileName)}"
                    };

                    mediaDbContext.Media.Add(new DbMedia(media));
                    blobs.Add((dbAttachment.ContentId, media.ContentId));
                    dbAttachment.MediaId = mediaId;
                    dbAttachment.MetadataJson = "";
                    dbAttachment.ContentId = "";
                }

                log.LogInformation("- Saving changes");

                await mediaDbContext.SaveChangesAsync().ConfigureAwait(false);

                foreach (var blob in blobs) {
                    await blobStorage.Copy(blob.oldPath, blob.newPath, CancellationToken.None).ConfigureAwait(false);
                }

                await dbContext.SaveChangesAsync().ConfigureAwait(false);

                await blobStorage.Delete(blobs.ConvertAll(x => x.oldPath), CancellationToken.None).ConfigureAwait(false);

                if (dbAttachments.Count < take)
                    break;

                skip += take;
                blobs.Clear();
            }

            log.LogInformation("Upgrading attachments: done");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
