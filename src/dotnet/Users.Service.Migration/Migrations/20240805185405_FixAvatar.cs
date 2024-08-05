using ActualChat.Chat.Module;
using ActualChat.Db;
using ActualChat.Hosting;
using ActualChat.Media.Module;
using ActualChat.Users.Module;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable MA0004
#nullable disable

namespace ActualChat.Users.Migrations
{
    /// <inheritdoc />
    public partial class FixAvatar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            UpAsync(migrationBuilder).Wait();
        }

        private async Task UpAsync(MigrationBuilder migrationBuilder)
        {
            var dbInitializer = DbInitializer.GetCurrent<UsersDbInitializer>();
            var log = dbInitializer.Services.LogFor(GetType());

            await using var dbContext = dbInitializer.CreateDbContext(true);

            var dbAvatars = await dbContext.Avatars
                .Where(x => x.Picture.StartsWith("https://source.boringavatars.com"))
                .OrderBy(x => x.Id)
                .ToListAsync();
            log.LogInformation("Fixing picture for {Count} avatars", dbAvatars.Count);
            foreach (var dbAvatar in dbAvatars) {
                // https://source.boringavatars.com/beam/160/D2F20C69E5CFE0BE2EB85F472EF92CDFABCBFEB4?colors=FFDBA0,BBBEFF,9294E1,FF9BC0,0F2FE8
                dbAvatar.AvatarKey = dbAvatar.Picture
                    .Replace("https://source.boringavatars.com/beam/160/", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("?colors=FFDBA0,BBBEFF,9294E1,FF9BC0,0F2FE8", "", StringComparison.OrdinalIgnoreCase);
                dbAvatar.Picture = "";
                log.LogInformation("- Fixed picture for {AvatarId}", dbAvatar.Id);
                continue;
            }
            log.LogInformation("- Saving changes");
            await dbContext.SaveChangesAsync();
            log.LogInformation("Upgrading avatars: done");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
