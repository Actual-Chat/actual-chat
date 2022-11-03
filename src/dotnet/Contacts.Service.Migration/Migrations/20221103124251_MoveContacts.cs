using System;
using ActualChat.Contacts.Db;
using ActualChat.Db;
using ActualChat.Hosting;
using ActualChat.Users.Module;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Contacts.Migrations
{
    public partial class MoveContacts : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var dbInitializer = DbInitializer.Current as DbInitializer<ContactsDbContext>;
            var usersDbInitializer = dbInitializer.InitializeTasks
                .Select(kv => kv.Key is UsersDbInitializer x ? x : null)
                .SingleOrDefault(x => x != null);
            if (usersDbInitializer == null)
                return;

            var dbContext = dbInitializer.DbHub.CreateDbContext(true);
            var usersDbContext = usersDbInitializer.DbHub.CreateDbContext();
            var oldContacts = usersDbContext.Contacts.ToList();
            foreach (var oc in oldContacts) {
                if (oc.OwnerUserId.IsNullOrEmpty() || oc.TargetUserId.IsNullOrEmpty())
                    continue;

                var c = new DbContact() {
                    Id = DbContact.ComposeUserContactId(oc.OwnerUserId, oc.TargetUserId),
                    Version = oc.Version,
                    OwnerId = oc.OwnerUserId,
                    UserId = oc.TargetUserId,
                    ChatId = null,
                };
                dbContext.Add(c);
            }
            dbContext.SaveChanges();
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        { }
    }
}
