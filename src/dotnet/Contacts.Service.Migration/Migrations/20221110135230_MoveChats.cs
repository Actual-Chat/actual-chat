using ActualChat.Chat;
using ActualChat.Chat.Module;
using ActualChat.Contacts.Db;
using ActualChat.Db;
using ActualChat.Hosting;
using ActualChat.Users;
using ActualChat.Users.Module;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Contacts.Migrations
{
    public partial class MoveChats : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var dbInitializer = DbInitializer.Current as DbInitializer<ContactsDbContext>;
            var chatDbInitializer = dbInitializer.InitializeTasks
                .Select(kv => kv.Key is ChatDbInitializer x ? x : null)
                .SingleOrDefault(x => x != null);
            if (chatDbInitializer == null)
                return;

            var usersDbInitializer = dbInitializer.InitializeTasks
                .Select(kv => kv.Key is UsersDbInitializer x ? x : null)
                .SingleOrDefault(x => x != null);
            if (usersDbInitializer == null)
                return;

            var clocks = dbInitializer.Services.Clocks();
            var versionGenerator = dbInitializer.DbHub.VersionGenerator;

            using var dbContext = dbInitializer.DbHub.CreateDbContext(true);
            using var chatDbContext = chatDbInitializer.DbHub.CreateDbContext();
            using var usersDbContext = usersDbInitializer.DbHub.CreateDbContext();

            // Removing all existing chat DbContacts
            var dbContacts = dbContext.Contacts.Where(c => c.ChatId != null && c.ChatId != "").ToList();
            dbContext.Contacts.RemoveRange(dbContacts);
            dbContext.SaveChanges();

            // And recreating them
            var dbAccounts = usersDbContext.Accounts.ToDictionary(c => (Symbol)c.Id);
            var dbChats = chatDbContext.Chats.ToDictionary(c => (Symbol)c.Id);
            var dbAuthors = chatDbContext.Authors.Where(a => !a.HasLeft).ToList();
            foreach (var dbAuthor in dbAuthors) {
                var userId = new UserId(dbAuthor.UserId, AssumeValid.Option);
                if (userId.IsNone) // Anonymous author, we do nothing in this case
                    continue;

                var chat = dbChats.GetValueOrDefault(dbAuthor.ChatId);
                if (chat == null) // No chat
                    continue;
                if (chat.Kind != ChatKind.Group) // Not a group chat
                    continue;

                var c = new DbContact() {
                    Id = new ContactId(userId, new ChatId(chat.Id), AssumeValid.Option),
                    Version = versionGenerator.NextVersion(),
                    OwnerId = userId,
                    UserId = null,
                    ChatId = chat.Id,
                    TouchedAt = clocks.SystemClock.Now,
                };
                dbContext.Add(c);
            }
            dbContext.SaveChanges();
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        { }
    }
}
