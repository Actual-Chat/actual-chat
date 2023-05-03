using ActualChat.Db;
using ActualChat.Hosting;
using ActualChat.Media.Db;
using ActualChat.Media.Module;
using ActualChat.Users.Module;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CS0618
#pragma warning disable VSTHRD002

namespace ActualChat.Users.Migrations
{
    /// <inheritdoc />
    public partial class MigrateAvatars : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // NOTE(DF): Obsolete: applied to all of our DBs. Now it causes migration in tests to fail.
            //UpAsync(migrationBuilder).Wait();
        }

        // private async Task UpAsync(MigrationBuilder migrationBuilder)
        // {
        //     var dbInitializer = DbInitializer.Get<UsersDbInitializer>();
        //     var mediaDbInitializer = await DbInitializer.Get<MediaDbInitializer>()
        //         .CompleteEarlierMigrations(this)
        //         .ConfigureAwait(false);
        //     var log = dbInitializer.Services.LogFor(GetType());
        //
        //     var blobStorageProvider = dbInitializer.Services.GetRequiredService<IBlobStorageProvider>();
        //     var blobStorage = blobStorageProvider.GetBlobStorage(BlobScope.ContentRecord);
        //
        //     using var dbContext = dbInitializer.DbHub.CreateDbContext(true);
        //     using var mediaDbContext = mediaDbInitializer.DbHub.CreateDbContext(true);
        //     mediaDbContext.ChangeTracker.AutoDetectChangesEnabled = false;
        //
        //     var dbAvatars = await dbContext.Avatars
        //         .Where(x => x.Picture != null)
        //         .Where(x => x.Picture != "")
        //         .OrderBy(c => c.Id)
        //         .ToListAsync()
        //         .ConfigureAwait(false);
        //
        //     log.LogInformation("Upgrading {Count} avatars...", dbAvatars.Count);
        //
        //     var blobs = new List<(string OldPath, string NewPath)>(dbAvatars.Count);
        //
        //     foreach (var dbAvatar in dbAvatars) {
        //         if (dbAvatar.Picture.StartsWith("http", StringComparison.InvariantCultureIgnoreCase))
        //             continue;
        //
        //         var mediaId = new MediaId(dbAvatar.UserId ?? "avatar", Generate.Option);
        //         var hashCode = mediaId.Id.ToString().GetSHA256HashCode();
        //         var media = new Media.Media {
        //             Id = mediaId,
        //             ContentId = $"media/{hashCode}/{mediaId.LocalId}{Path.GetExtension(dbAvatar.Picture)}",
        //         };
        //
        //         mediaDbContext.Media.Add(new DbMedia(media));
        //         blobs.Add((dbAvatar.Picture, media.ContentId));
        //         dbAvatar.MediaId = mediaId;
        //         dbAvatar.Picture = "";
        //     }
        //
        //     log.LogInformation("Saving Media DB changes");
        //     await mediaDbContext.SaveChangesAsync().ConfigureAwait(false);
        //
        //     await Parallel.ForEachAsync(blobs,
        //         new ParallelOptions() { MaxDegreeOfParallelism = 4 },
        //         async (blob, ct) => {
        //             var isCopied = await blobStorage.CopyIfExists(blob.OldPath, blob.NewPath, ct).ConfigureAwait(false);
        //             if (!isCopied)
        //                 log.LogWarning("Couldn't copy blob: {Blob}", blob.OldPath);
        //         }).ConfigureAwait(false);
        //
        //     log.LogInformation("Saving Avatars DB changes");
        //     await dbContext.SaveChangesAsync().ConfigureAwait(false);
        //
        //     await Parallel.ForEachAsync(blobs,
        //         new ParallelOptions() { MaxDegreeOfParallelism = 4 },
        //         async (blob, ct) => await blobStorage.DeleteIfExists(blob.OldPath, ct).ConfigureAwait(false)
        //     ).ConfigureAwait(false);
        //
        //     log.LogInformation("Upgrading avatars: done");
        // }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        { }
    }
}
