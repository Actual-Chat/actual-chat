using ActualChat.Chat;
using ActualChat.Chat.Module;
using ActualChat.Contacts.Db;
using ActualChat.Db;
using ActualChat.Hosting;
using ActualChat.Users;
using ActualChat.Users.Module;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Stl.Reflection;

#nullable disable
#pragma warning disable MA0004
#pragma warning disable VSTHRD002

namespace ActualChat.Contacts.Migrations
{
    /// <inheritdoc />
    public partial class UpgradeContacts2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // NOTE(EK): Obsolete: applied to all of our DBs. Now it causes migration in tests to fail.
            //UpAsync(migrationBuilder).Wait();
        }

        // private async Task UpAsync(MigrationBuilder migrationBuilder)
        // {
        //     var dbInitializer = DbInitializer.GetCurrent<ContactsDbInitializer>();
        //     var log = dbInitializer.Services.LogFor(GetType());
        //
        //     var clocks = dbInitializer.Services.Clocks();
        //
        //     using var dbContext = dbInitializer.CreateDbContext(true);
        //
        //     var dbContacts = await dbContext.Contacts.OrderBy(c => c.Id).ToListAsync().ConfigureAwait(false);
        //     log.LogInformation("Upgrading {Count} contacts", dbContacts.Count);
        //     var changeCount = 0;
        //     foreach (var contact in dbContacts) {
        //         var id = contact.Id;
        //         if (id.Split(' ') is not [ var sOwnerId, var tail ])
        //             goto skip;
        //         var ownerId = new UserId(sOwnerId);
        //         if (tail.Split(':') is not [ var type, var otherId])
        //             goto skip;
        //
        //         var newId = id;
        //         switch (type) {
        //         case "c":
        //             var chatId = new ChatId(otherId);
        //             newId = new ContactId(ownerId, chatId);
        //             break;
        //         case "u":
        //             var userId = new UserId(otherId);
        //             var peerChatId = new PeerChatId(ownerId, userId);
        //             newId = new ContactId(ownerId, peerChatId);
        //             break;
        //         default:
        //             goto skip;
        //         }
        //
        //         var newContact = MemberwiseCloner.Invoke(contact);
        //         newContact.Id = newId;
        //         dbContext.Contacts.Add(newContact);
        //         dbContext.Contacts.Remove(contact);
        //         changeCount++;
        //         log.LogInformation("- '{Id}': new Id = '{NewId}'", id, newId);
        //         continue;
        //     skip:
        //         continue;
        //         // log.LogInformation("- '{Id}': skipped", id);
        //     }
        //     log.LogInformation("- Saving changes");
        //     await dbContext.SaveChangesAsync().ConfigureAwait(false);
        //     log.LogInformation("Upgrading contacts: {ChangeCount} / {Count} upgraded", changeCount, dbContacts.Count);
        // }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        { }
    }
}
