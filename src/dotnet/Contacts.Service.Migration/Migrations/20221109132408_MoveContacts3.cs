using ActualChat.Contacts.Db;
using ActualChat.Db;
using ActualChat.Hosting;
using ActualChat.Users.Module;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Contacts.Migrations
{
    public partial class MoveContacts3 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var dbInitializer = DbInitializer.Current as DbInitializer<ContactsDbContext>;
            var usersDbInitializer = dbInitializer.InitializeTasks
                .Select(kv => kv.Key is UsersDbInitializer x ? x : null)
                .SingleOrDefault(x => x != null);
            if (usersDbInitializer == null)
                return;

            var clocks = dbInitializer.Services.Clocks();
            using var dbContext = dbInitializer.DbHub.CreateDbContext(true);
            using var usersDbContext = usersDbInitializer.DbHub.CreateDbContext();

            // Removing all existing DbContacts
            var dbContacts = dbContext.Contacts.ToList();
            dbContext.Contacts.RemoveRange(dbContacts);
            dbContext.SaveChanges();

            // And recreating them
            var oldContacts = usersDbContext.Contacts.ToList();
            foreach (var oc in oldContacts) {
                if (oc.OwnerUserId.IsNullOrEmpty() || oc.TargetUserId.IsNullOrEmpty())
                    continue;

                var c = new DbContact() {
                    Id = new ContactId(oc.OwnerUserId, oc.TargetUserId, ContactKind.User),
                    Version = oc.Version,
                    OwnerId = oc.OwnerUserId,
                    UserId = oc.TargetUserId,
                    ChatId = null,
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
