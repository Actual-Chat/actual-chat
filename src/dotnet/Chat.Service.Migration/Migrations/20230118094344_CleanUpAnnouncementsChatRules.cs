using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    /// <inheritdoc />
    public partial class CleanUpAnnouncementsChatRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
update roles
set can_invite = false,
    can_join = false
where chat_id = 'announcements'
    and system_role = 11; -- anyone
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
update roles
set can_invite = true,
    can_join = true
where chat_id = 'announcements'
    and system_role = 11; -- anyone
");
        }
    }
}
