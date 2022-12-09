using ActualChat.Chat;
using ActualChat.Chat.Module;
using ActualChat.Contacts.Db;
using ActualChat.Db;
using ActualChat.Hosting;
using ActualChat.Users;
using ActualChat.Users.Module;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable MA0004
#pragma warning disable VSTHRD002

namespace ActualChat.Contacts.Migrations
{
    /// <inheritdoc />
    public partial class UpgradeContacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            UpAsync(migrationBuilder).Wait();
        }

        private async Task UpAsync(MigrationBuilder migrationBuilder)
        {
            var dbInitializer = DbInitializer.Current as DbInitializer<ContactsDbContext>;
            var log = dbInitializer.Services.LogFor(GetType());

            var clocks = dbInitializer.Services.Clocks();
            var versionGenerator = dbInitializer.DbHub.VersionGenerator;

            using var dbContext = dbInitializer.DbHub.CreateDbContext(true);

            var dbContacts = await dbContext.Contacts.OrderBy(c => c.Id).ToListAsync();
            log.LogInformation("Upgrading {Count} contacts", dbContacts.Count);
            var changeCount = 0;
            foreach (var c in dbContacts) {
                var id = c.Id;
                if (id.Split(' ') is not [ var sOwnerId, var tail ])
                    goto skip;
                var ownerId = new UserId(sOwnerId);
                if (tail.Split(':') is not [ var type, var otherId])
                    goto skip;

                switch (type) {
                case "c":
                    var chatId = new ChatId(otherId);
                    c.Id = new ContactId(ownerId, chatId).Value;
                    break;
                case "u":
                    var userId = new UserId(otherId);
                    var peerChatId = new PeerChatId(ownerId, userId);
                    c.Id = new ContactId(ownerId, peerChatId).Value;
                    break;
                default:
                    goto skip;
                }

                changeCount++;
                log.LogInformation("- '{Id}': new Id = '{NewId}'", id, c.Id);
                continue;
            skip:
                continue;
                // log.LogInformation("- '{Id}': skipped", id);
            }
            log.LogInformation("- Saving changes");
            await dbContext.SaveChangesAsync();
            log.LogInformation("Upgrading contacts: {ChangeCount} / {Count} upgraded", changeCount, dbContacts.Count);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        { }
    }
}
