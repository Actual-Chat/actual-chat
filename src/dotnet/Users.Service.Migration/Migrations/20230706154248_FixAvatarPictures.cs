using ActualChat.Chat.Module;
using ActualChat.Db;
using ActualChat.Hosting;
using ActualChat.Media.Module;
using ActualChat.Users.Module;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable MA0004
#pragma warning disable VSTHRD002
#pragma warning disable CA1847
#pragma warning disable CS0162
#nullable disable

namespace ActualChat.Users.Migrations
{
    /// <inheritdoc />
    public partial class FixAvatarPictures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            UpAsync(migrationBuilder).Wait();
        }

        private async Task UpAsync(MigrationBuilder migrationBuilder)
        {
            var dbInitializer = DbInitializer.GetCurrent<UsersDbInitializer>();
            var mediaDbInitializer = await DbInitializer.GetOther<MediaDbInitializer>()
                .CompleteEarlierMigrations(this);
            var log = dbInitializer.Services.LogFor(GetType());

            var clocks = dbInitializer.Services.Clocks();

            using var dbContext = dbInitializer.CreateDbContext(true);
            using var mediaDbContext = mediaDbInitializer.CreateDbContext(true);

            var dbAvatars = await dbContext.Avatars
                .Where(a => !string.IsNullOrEmpty(a.Picture))
                .Where(a => !a.Picture.StartsWith("http"))
                .OrderBy(c => c.Id)
                .ToListAsync();
            log.LogInformation("Fixing picture for {Count} avatars", dbAvatars.Count);
            foreach (var dbAvatar in dbAvatars) {
                var contentId = dbAvatar.Picture;
                if (dbAvatar.MediaId.IsNullOrEmpty()) {
                    var media = await mediaDbContext.Media.FirstOrDefaultAsync(x => x.ContentId == contentId);
                    dbAvatar.MediaId = media?.Id;
                }
                if (!dbAvatar.MediaId.IsNullOrEmpty()) {
                    dbAvatar.Picture = "";
                }
                log.LogInformation("- Fixed picture for {AvatarId}", dbAvatar.Id);
                continue;
            }
            log.LogInformation("- Saving changes");
            await dbContext.SaveChangesAsync();
            log.LogInformation("Upgrading avatars: done");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        { }
    }
}
