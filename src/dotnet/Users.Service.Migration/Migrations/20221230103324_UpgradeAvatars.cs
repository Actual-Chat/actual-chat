using ActualChat.Chat;
using ActualChat.Chat.Module;
using ActualChat.Db;
using ActualChat.Hosting;
using ActualChat.Users;
using ActualChat.Users.Db;
using ActualChat.Users.Module;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Stl.Reflection;

#pragma warning disable MA0004
#pragma warning disable VSTHRD002
#pragma warning disable CA1847
#pragma warning disable CS0162

namespace ActualChat.Users.Migrations
{
    /// <inheritdoc />
    public partial class UpgradeAvatars : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            UpAsync(migrationBuilder).Wait();
        }

        private async Task UpAsync(MigrationBuilder migrationBuilder)
        {
            return; // Obsolete: applied to all of our DBs

            var dbInitializer = DbInitializer.GetCurrent<UsersDbInitializer>();
            var chatDbInitializer = await DbInitializer.GetOther<ChatDbInitializer>()
                .CompleteEarlierMigrations(this)
                .ConfigureAwait(false);
            var log = dbInitializer.Services.LogFor(GetType());

            var clocks = dbInitializer.Services.Clocks();

            using var dbContext = dbInitializer.CreateDbContext(true);
            using var chatDbContext = chatDbInitializer.CreateDbContext(true);

            var dbAvatars = await dbContext.Avatars
                .Where(a => (a.UserId ?? "").Contains(":"))
                .OrderBy(c => c.Id)
                .ToListAsync()
                .ConfigureAwait(false);
            log.LogInformation("Upgrading {Count} avatars", dbAvatars.Count);
            foreach (var dbAvatar in dbAvatars) {
                var id = dbAvatar.Id;
                var authorId = new AuthorId(dbAvatar.UserId, ParseOrNone.Option);
                var userId = "";
                if (!authorId.IsNone) {
                    var dbAuthor = await chatDbContext.Authors
                        .SingleOrDefaultAsync(a => a.Id == authorId)
                        .ConfigureAwait(false);
                    userId = dbAuthor?.UserId;
                }

                userId = userId.NullIfEmpty();
                if (userId == null)
                    log.LogWarning("- '{Id}': UserId = null (was '{OldUserId}')", id, dbAvatar.UserId);
                else
                    log.LogInformation("- '{Id}': UserId = '{NewUserId}' (was '{OldUserId}')", id, userId, dbAvatar.UserId);

                dbAvatar.UserId = userId.NullIfEmpty();
                continue;
            }
            log.LogInformation("- Saving changes");
            await dbContext.SaveChangesAsync().ConfigureAwait(false);
            log.LogInformation("Upgrading avatars: done");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        { }
    }
}
