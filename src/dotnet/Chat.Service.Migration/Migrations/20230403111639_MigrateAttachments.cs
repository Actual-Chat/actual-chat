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
            var dbInitializer = DbInitializer.GetCurrent<ChatDbInitializer>();
            var mediaDbInitializer = await DbInitializer.GetOther<MediaDbInitializer>()
                .CompleteEarlierMigrations(this)
                .ConfigureAwait(false);
            var log = dbInitializer.Services.LogFor(GetType());
            var blobStorageProvider = dbInitializer.Services.GetRequiredService<IBlobStorageProvider>();
            var blobStorage = blobStorageProvider.GetBlobStorage(BlobScope.ContentRecord);

            var skip = 0;
            const int batchSize = 100;
            var blobs = new List<(string OldPath, string NewPath)>(batchSize);

            while (true) {
                using var dbContext = dbInitializer.CreateDbContext(true);
                using var mediaDbContext = mediaDbInitializer.CreateDbContext(true);
                mediaDbContext.ChangeTracker.AutoDetectChangesEnabled = false;

                var dbAttachments = await dbContext.TextEntryAttachments
                    .OrderBy(c => c.Id)
                    .Skip(skip)
                    .Take(batchSize)
                    .ToListAsync()
                    .ConfigureAwait(false);

                if (!dbAttachments.Any())
                    break;

                log.LogInformation("Upgrading {Count} attachments", dbAttachments.Count);

                foreach (var dbAttachment in dbAttachments) {
                    if (dbAttachment.ContentId.IsNullOrEmpty())
                        continue;

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

                log.LogInformation("Saving Media DB changes");
                await mediaDbContext.SaveChangesAsync().ConfigureAwait(false);

                await Parallel.ForEachAsync(blobs,
                    new ParallelOptions() { MaxDegreeOfParallelism = 4 },
                    async (blob, ct) => {
                        var isCopied = await blobStorage.CopyIfExists(blob.OldPath, blob.NewPath, ct).ConfigureAwait(false);
                        if (!isCopied)
                            log.LogWarning("Couldn't copy blob: {Blob}", blob.OldPath);
                    }).ConfigureAwait(false);

                log.LogInformation("Saving Chats DB changes");
                await dbContext.SaveChangesAsync().ConfigureAwait(false);

                await Parallel.ForEachAsync(blobs,
                    new ParallelOptions() { MaxDegreeOfParallelism = 4 },
                    async (blob, ct) => await blobStorage.DeleteIfExists(blob.OldPath, ct).ConfigureAwait(false)
                ).ConfigureAwait(false);

                if (dbAttachments.Count < batchSize)
                    break;

                skip += batchSize;
                blobs.Clear();
            }

            log.LogInformation("Upgrading attachments: done");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        { }
    }
}
