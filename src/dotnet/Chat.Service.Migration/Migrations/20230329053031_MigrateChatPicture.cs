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
    public partial class MigrateChatPicture : Migration
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

            var blobStorage = dbInitializer.Services.GetRequiredService<IBlobStorage>();

            using var dbContext = dbInitializer.DbHub.CreateDbContext(true);
            using var mediaDbContext = mediaDbInitializer.DbHub.CreateDbContext(true);
            mediaDbContext.ChangeTracker.AutoDetectChangesEnabled = false;

            var dbChats = await dbContext.Chats
                .Where(x => x.Picture != null)
                .Where(x => x.Picture != "")
                .OrderBy(c => c.Id)
                .ToListAsync()
                .ConfigureAwait(false);

            log.LogInformation("Upgrading {Count} chats", dbChats.Count);

            var blobs = new List<(string oldPath, string newPath)>(dbChats.Count);

            foreach (var dbChat in dbChats) {
                var mediaId = new MediaId(dbChat.Id, Generate.Option);
                var hashCode = mediaId.Id.ToString().GetSHA256HashCode();
                var media = new Media.Media {
                    Id = mediaId,
                    ContentId = $"media/{hashCode}/{mediaId.LocalId}{Path.GetExtension(dbChat.Picture)}",
                };

                mediaDbContext.Media.Add(new DbMedia(media));
                blobs.Add((dbChat.Picture, media.ContentId));
                dbChat.MediaId = mediaId;
                dbChat.Picture = "";
            }

            log.LogInformation("- Saving changes");

            await mediaDbContext.SaveChangesAsync().ConfigureAwait(false);

            foreach (var blob in blobs) {
                await blobStorage.Copy(blob.oldPath, blob.newPath, CancellationToken.None).ConfigureAwait(false);
            }

            await dbContext.SaveChangesAsync().ConfigureAwait(false);

            await blobStorage.Delete(blobs.ConvertAll(x => x.oldPath), CancellationToken.None).ConfigureAwait(false);

            log.LogInformation("Upgrading chats: done");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
